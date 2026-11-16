using Weftone.Abstractions.Plugins;
using Weftone.Abstractions.Rendering;

namespace Weftone.Engine;

/// <summary>
/// RenderPlan 离线/实时两用渲染器：64 复音预分配池，事件按 StartSample 顺序激活，
/// 池满时最早结束者被偷（FadeTail 线性淡出防 click），新事件入 pending 环等位。
/// Sine：双精度相位；Sampler：资产线性插值、MIDI60=原速、资产耗尽自然结束。
/// 自动化（SPEC M2）：声部按通道 GainBreakpoints 曲线逐样本线性斜坡（vol/pan 预乘、
/// velocity 留在 GainLeft），master 曲线同理后乘；游标循环回绕二分重定位。
/// RCU 交换（SPEC M2 竖切 4）：池声部直接持有资产/曲线引用（plan 不可变 ⇒ 换 plan
/// 不影响在飞声部）；SwapPlan 只做 volatile 引用写入，游标在音频线程内按当前绝对
/// 位置惰性二分重定位，transport 不重置——播放中编辑无静音缺口；全程零分配。
/// 插件声部（SPEC M3 D5）：plan+实例表打包为 State 原子换入（配对防 slot 错位）；
/// note on/off 区间事件按 256 帧 chunk 派发插件 process，输出乘通道静态增益入总线
/// （插件通道的 vol/pan 曲线走静态值，自动化精度留竖切 4）；全程零分配。
/// </summary>
public sealed class PlanRenderer : IPlanRenderer
{
    private const int MaxVoices = 64;
    private const int FadeTail = 128;
    private const int PendingCapacity = 32;
    private const double TwoPi = 6.283185307179586476925286766559;
    private const int PluginChunkFrames = 256;
    private const int ChannelBufferFrames = 8192;
    private const int MaxActivePluginNotes = 256;

    /// <summary>plan 与插件实例表原子束：一次 volatile 引用换入，避免新 plan 配旧表的 slot 错位。</summary>
    private sealed class State
    {
        public RenderPlan? Plan;
        public IAudioPluginInstance[] Plugins = [];
        /// <summary>每通道效果链（SPEC M5 D2）：无效果链的通道为空数组。</summary>
        public Effects.IEffectProcessor?[][] ChannelEffects = [];
        /// <summary>每通道 interleaved 缓冲（效果链挂板，预分配）。</summary>
        public float[][] ChannelBuffers = [];
        /// <summary>每节点参数工作副本（曲线写此不触 plan）。</summary>
        public double[][] EffectParameters = [];
        /// <summary>每槽事件 scratch（表构造线程预分配，音频线程只写不扩）。</summary>
        public PluginEvent[][] Scratch = [];
        /// <summary>每槽最近通道增益（note off 后的 release 尾巴沿用；同通道同值）。</summary>
        public float[] SlotGainLeft = [];
        public float[] SlotGainRight = [];
    }

    private struct PluginVoice
    {
        public long Start;
        public long End;
        public int Slot;
        public int Key;
        public int Channel;
        public float Velocity;
        public float GainLeft;
        public float GainRight;
    }

    private struct SegmentParamEvent
    {
        public int Slot;
        public uint ParamId;
        public long Sample;
        public double Value;
    }

    private struct Voice
    {
        public bool Active;
        public int ChannelIndex;
        public long Wait;
        public long Remaining;
        public double Phase;
        public double PhaseDelta;
        public double AssetPosition;
        public PlanAsset? Asset;
        public float Frequency;
        public float GainLeft;
        public float GainRight;
        public RenderVoiceKind Kind;
        public int StealFade;
        public GainBreakpoint[]? GainCurve;
        public int GainCursor;
    }

    private readonly Voice[] _pool = new Voice[MaxVoices];
    private readonly RenderVoice[] _pending = new RenderVoice[PendingCapacity];
    private readonly PluginVoice[] _pluginNotes = new PluginVoice[MaxActivePluginNotes];
    private readonly SegmentParamEvent[] _segmentParamEvents = new SegmentParamEvent[MaxActivePluginNotes];
    private int _segmentParamEventCount;
    private int _nextParamEvent;
    private readonly float[] _pluginLeft = new float[PluginChunkFrames];
    private readonly float[] _pluginRight = new float[PluginChunkFrames];
    private int _pendingHead;
    private int _pendingCount;
    private int _pluginNoteCount;
    private volatile State _state = new();
    /// <summary>游标/循环状态所属 plan（音频线程内与当前 plan 快照比对，检测交换后重定位）。</summary>
    private RenderPlan? _cursorPlan;
    private int _nextEvent;
    private long _position;
    private long _loopStart;
    private long _loopEnd;
    private int _masterGainCursor;

    public long Position => _position;

    public long DroppedEvents { get; private set; }

    public void Load(RenderPlan plan, IReadOnlyList<IAudioPluginInstance>? plugins = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _state = BuildState(plan, plugins ?? []);
        _position = 0;
        _pluginNoteCount = 0;
        _segmentParamEventCount = 0;
        _nextParamEvent = 0;
        DroppedEvents = 0;
        Array.Clear(_pool);
        ResyncCursor(plan);
    }

    /// <summary>RCU 交换：State 原子换入（plan+实例表配对）；重定位在音频线程 Render 内惰性完成。</summary>
    public void SwapPlan(RenderPlan plan, IReadOnlyList<IAudioPluginInstance>? plugins = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        State previous = _state;
        _state = plugins is null
            ? BuildState(plan, previous.Plugins)
            : BuildState(plan, plugins);
    }

    private static State BuildState(RenderPlan plan, IReadOnlyList<IAudioPluginInstance> plugins)
    {
        var state = new State
        {
            Plan = plan,
            Plugins = [.. plugins],
            Scratch = new PluginEvent[plugins.Count][],
            SlotGainLeft = new float[plugins.Count],
            SlotGainRight = new float[plugins.Count],
        };
        for (int i = 0; i < plugins.Count; i++)
        {
            state.Scratch[i] = new PluginEvent[PluginChunkFrames];
            state.SlotGainLeft[i] = 1f;
            state.SlotGainRight[i] = 1f;
        }
        int channels = Math.Max(1, plan.ChannelCount);
        var chains = new List<Effects.IEffectProcessor?>[channels];
        for (int c = 0; c < channels; c++)
        {
            chains[c] = [];
        }
        foreach (Abstractions.Plugins.RenderEffectNode node in plan.EffectNodes)
        {
            if (node.Bypassed || node.ChannelIndex < 0 || node.ChannelIndex >= channels)
            {
                continue;
            }
            chains[node.ChannelIndex].Add(Effects.EffectProcessorFactory.Create(node.Kind, node.Parameters));
        }
        state.ChannelEffects = new Effects.IEffectProcessor?[channels][];
        state.ChannelBuffers = new float[channels][];
        state.EffectParameters = new double[plan.EffectNodes.Length][];
        for (int n = 0; n < plan.EffectNodes.Length; n++)
        {
            state.EffectParameters[n] = [.. plan.EffectNodes[n].Parameters];
        }
        for (int c = 0; c < channels; c++)
        {
            state.ChannelEffects[c] = [.. chains[c]];
            state.ChannelBuffers[c] = state.ChannelEffects[c].Length > 0
                ? new float[ChannelBufferFrames * 2]
                : [];
        }
        return state;
    }

    public void Render(Span<float> interleaved)
    {
        interleaved.Clear();
        State state = _state;
        RenderPlan? plan = state.Plan;
        if (plan is null)
        {
            return;
        }
        if (!ReferenceEquals(plan, _cursorPlan))
        {
            ResyncCursor(plan);
        }
        int frames = interleaved.Length / 2;
        if (frames == 0)
        {
            return;
        }
        int rendered = 0;
        while (rendered < frames)
        {
            long segment = frames - rendered;
            if (_loopEnd > _loopStart)
            {
                long tillEnd = _loopEnd - _position;
                if (tillEnd <= 0)
                {
                    // 回绕：位置回 loopStart，事件游标归零重触发；池声部延续尾音，
                    // 插件 note 窗口重置（事件从头重放，插件内部声部自行延续）
                    _position = _loopStart;
                    _nextEvent = 0;
                    _pluginNoteCount = 0;
                    _nextParamEvent = 0;
                    tillEnd = _loopEnd - _position;
                }
                if (tillEnd > 0 && tillEnd < segment)
                {
                    segment = tillEnd;
                }
            }
            if (segment > ChannelBufferFrames)
            {
                segment = ChannelBufferFrames; // 效果链缓冲上限：超长段拆分
            }
            RenderSegment(state, plan, interleaved, rendered, (int)segment);
            rendered += (int)segment;
        }

        if (plan.MasterGainCurve is null && plan.MasterGain != 1f)
        {
            float master = plan.MasterGain;
            for (int i = 0; i < interleaved.Length; i++)
            {
                interleaved[i] *= master;
            }
        }
    }

    /// <summary>换 plan 后重定位：事件游标二分到当前绝对位置（playhead 前的事件不补触发）、
    /// 放弃旧 plan 的 pending 溢出事件与在飞插件 note 窗口（slot 映射已变，旧 off 不再可靠——
    /// 已知限制见 SPEC M3 风险）、更新循环区间；仅音频线程调用。</summary>
    private void ResyncCursor(RenderPlan plan)
    {
        _cursorPlan = plan;
        DroppedEvents += _pendingCount;
        _pendingHead = 0;
        _pendingCount = 0;
        _pluginNoteCount = 0;
        _loopStart = plan.LoopStartSample;
        _loopEnd = plan.LoopEndSample;
        if (_masterGainCursor >= plan.MasterGainCurve?.Length)
        {
            _masterGainCursor = 0;
        }
        _nextEvent = LowerBoundVoice(plan.Voices, _position);
        RenderPluginParamEvent[] paramEvents = plan.PluginParamEvents;
        int plow = 0, phigh = paramEvents.Length;
        while (plow < phigh)
        {
            int mid = plow + (phigh - plow) / 2;
            if (paramEvents[mid].Sample < _position)
            {
                plow = mid + 1;
            }
            else
            {
                phigh = mid;
            }
        }
        _nextParamEvent = plow;
    }

    private static int LowerBoundVoice(RenderVoice[] voices, long position)
    {
        int low = 0, high = voices.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (voices[mid].StartSample < position)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        return low;
    }

    private void RenderSegment(State state, RenderPlan plan, Span<float> interleaved, int offsetFrames, int frames)
    {
        Span<float> slice = interleaved.Slice(offsetFrames * 2, frames * 2);
        long segmentStart = _position;
        long end = segmentStart + frames;

        while (_nextEvent < plan.Voices.Length && plan.Voices[_nextEvent].StartSample < end)
        {
            RenderVoice voice = plan.Voices[_nextEvent];
            if (voice.Kind == RenderVoiceKind.Plugin)
            {
                if (_pluginNoteCount < _pluginNotes.Length)
                {
                    _pluginNotes[_pluginNoteCount++] = new PluginVoice
                    {
                        Start = voice.StartSample,
                        End = voice.StartSample + voice.LengthSamples,
                        Slot = voice.PluginSlot,
                        Key = voice.NoteKey,
                        Channel = 0,
                        Velocity = voice.Velocity,
                        GainLeft = voice.GainLeft,
                        GainRight = voice.GainRight,
                    };
                }
                else
                {
                    DroppedEvents++;
                }
            }
            else
            {
                Activate(voice, plan);
            }
            _nextEvent++;
        }

        // 效果链通道缓冲清零（SPEC M5 D2：voice 按通道分组，效果后混入总线）
        bool hasEffects = state.ChannelBuffers.Length > 0;
        int nodeGlobalIndex = 0;
        if (hasEffects)
        {
            for (int c = 0; c < state.ChannelBuffers.Length; c++)
            {
                if (state.ChannelBuffers[c].Length > 0)
                {
                    state.ChannelBuffers[c].AsSpan(0, frames * 2).Clear();
                }
            }
        }

        for (int v = 0; v < _pool.Length; v++)
        {
            ref Voice voice = ref _pool[v];
            if (!voice.Active)
            {
                continue;
            }
            Span<float> target = slice;
            if (hasEffects && voice.ChannelIndex >= 0
                && voice.ChannelIndex < state.ChannelBuffers.Length
                && state.ChannelBuffers[voice.ChannelIndex].Length > 0)
            {
                target = state.ChannelBuffers[voice.ChannelIndex].AsSpan(0, frames * 2);
            }
            if (voice.StealFade > 0)
            {
                RenderFadeTail(ref voice, segmentStart, target, frames);
            }
            else
            {
                RenderGate(ref voice, segmentStart, target, frames);
            }
        }

        if (hasEffects)
        {
            for (int c = 0; c < state.ChannelBuffers.Length; c++)
            {
                float[] buffer = state.ChannelBuffers[c];
                if (buffer.Length == 0)
                {
                    continue;
                }
                Span<float> channelSpan = buffer.AsSpan(0, frames * 2);
                int nodeCursor = 0;
                foreach (Abstractions.Plugins.RenderEffectNode node in plan.EffectNodes)
                {
                    if (node.ChannelIndex != c || node.Bypassed)
                    {
                        nodeGlobalIndex++;
                        continue;
                    }
                    if (nodeCursor >= state.ChannelEffects[c].Length)
                    {
                        break;
                    }
                    Effects.IEffectProcessor? effect = state.ChannelEffects[c][nodeCursor++];
                    if (effect is null)
                    {
                        continue;
                    }
                    if (node.ParameterCurves is { } curves && nodeGlobalIndex < state.EffectParameters.Length)
                    {
                        double[] working = state.EffectParameters[nodeGlobalIndex];
                        for (int pi = 0; pi < curves.Length && pi < working.Length; pi++)
                        {
                            if (curves[pi] is not { } curve || curve.Length == 0)
                            {
                                continue;
                            }
                            working[pi] = EvaluateEffectCurve(curve, segmentStart);
                        }
                        effect.SetParameters(working);
                    }
                    effect.Process(channelSpan, frames);
                    nodeGlobalIndex++;
                }
                for (int i = 0; i < channelSpan.Length; i++)
                {
                    slice[i] += channelSpan[i];
                }
            }
        }

        // 参数事件点（竖切 4）：本段窗口内事件收集，段内按 chunk 派发
        _segmentParamEventCount = 0;
        RenderPluginParamEvent[] paramEvents = plan.PluginParamEvents;
        while (_nextParamEvent < paramEvents.Length && paramEvents[_nextParamEvent].Sample < end)
        {
            if (_segmentParamEventCount < _segmentParamEvents.Length)
            {
                RenderPluginParamEvent source = paramEvents[_nextParamEvent];
                _segmentParamEvents[_segmentParamEventCount++] = new SegmentParamEvent
                {
                    Slot = source.Slot,
                    ParamId = source.ParamId,
                    Sample = source.Sample,
                    Value = source.Value,
                };
            }
            else
            {
                DroppedEvents++;
            }
            _nextParamEvent++;
        }

        RenderPluginSegment(state, slice, frames, segmentStart);

        if (plan.MasterGainCurve is { } masterCurve)
        {
            for (int i = 0; i < frames; i++)
            {
                long position = segmentStart + i;
                AdvanceGainCursor(ref _masterGainCursor, masterCurve, position);
                InterpolateGain(masterCurve, _masterGainCursor, position, out float master, out _);
                slice[2 * i] *= master;
                slice[2 * i + 1] *= master;
            }
        }

        FillSlotsFromPending(plan);
        _position = end;
    }

    /// <summary>效果参数曲线块级取值：断点 bracket 线性插值（首末 clamp）。</summary>
    private static double EvaluateEffectCurve(GainBreakpoint[] curve, long position)
    {
        if (curve.Length == 1 || position <= curve[0].Sample)
        {
            return curve[0].GainLeft;
        }
        for (int i = 1; i < curve.Length; i++)
        {
            if (position < curve[i].Sample)
            {
                long span = curve[i].Sample - curve[i - 1].Sample;
                double t = span <= 0 ? 1 : (double)(position - curve[i - 1].Sample) / span;
                return curve[i - 1].GainLeft + (curve[i].GainLeft - curve[i - 1].GainLeft) * t;
            }
        }
        return curve[^1].GainLeft;
    }

    /// <summary>插件声部段渲染：note on/off 窗口按 256 帧 chunk 派发给实例，
    /// 事件按帧序（on 序 + off 序归并后插入排序），输出乘通道静态增益入总线；零分配。</summary>
    private void RenderPluginSegment(State state, Span<float> slice, int frames, long segmentStart)
    {
        if (_pluginNoteCount == 0 || state.Plugins.Length == 0)
        {
            // 无窗口仍需清理：note 都在本段结束（理论上 _pluginNoteCount>0 才有），早退安全
            if (_pluginNoteCount > 0)
            {
                RemoveFinishedPluginNotes(segmentStart + frames);
            }
            return;
        }
        int done = 0;
        while (done < frames)
        {
            int chunk = Math.Min(PluginChunkFrames, frames - done);
            long chunkStart = segmentStart + done;
            long chunkEnd = chunkStart + chunk;

            for (int slot = 0; slot < state.Plugins.Length; slot++)
            {
                PluginEvent[] scratch = state.Scratch[slot];
                int count = 0;
                bool slotActive = false;
                float gainLeft = state.SlotGainLeft[Math.Min(slot, state.SlotGainLeft.Length - 1)];
                float gainRight = state.SlotGainRight[Math.Min(slot, state.SlotGainRight.Length - 1)];
                for (int i = 0; i < _pluginNoteCount; i++)
                {
                    PluginVoice note = _pluginNotes[i];
                    if (note.Slot != slot)
                    {
                        continue;
                    }
                    slotActive = true;
                    gainLeft = note.GainLeft;
                    gainRight = note.GainRight;
                    if (count >= scratch.Length)
                    {
                        break;
                    }
                    if (note.Start >= chunkStart && note.Start < chunkEnd)
                    {
                        scratch[count++] = new PluginEvent(
                            PluginEventKind.NoteOn, (int)(note.Start - chunkStart), note.Key, note.Channel, note.Velocity, 0, 0);
                    }
                    if (note.End > chunkStart && note.End < chunkEnd && count < scratch.Length)
                    {
                        scratch[count++] = new PluginEvent(
                            PluginEventKind.NoteOff, (int)(note.End - chunkStart), note.Key, note.Channel, 0f, 0, 0);
                    }
                }
                for (int i = 0; i < _segmentParamEventCount && count < scratch.Length; i++)
                {
                    SegmentParamEvent param = _segmentParamEvents[i];
                    if (param.Slot != slot || param.Sample < chunkStart || param.Sample >= chunkEnd)
                    {
                        continue;
                    }
                    slotActive = true;
                    scratch[count++] = new PluginEvent(
                        PluginEventKind.ParamValue, (int)(param.Sample - chunkStart), 0, 0, 0f, param.ParamId, param.Value);
                }
                if (!slotActive)
                {
                    continue;
                }
                state.SlotGainLeft[Math.Min(slot, state.SlotGainLeft.Length - 1)] = gainLeft;
                state.SlotGainRight[Math.Min(slot, state.SlotGainRight.Length - 1)] = gainRight;
                SortPluginEvents(scratch, count);

                state.Plugins[slot].Process(
                    _pluginLeft.AsSpan(0, chunk), _pluginRight.AsSpan(0, chunk), scratch.AsSpan(0, count));

                int offset = done * 2;
                for (int i = 0; i < chunk; i++)
                {
                    slice[offset + 2 * i] += _pluginLeft[i] * gainLeft;
                    slice[offset + 2 * i + 1] += _pluginRight[i] * gainRight;
                }
            }
            done += chunk;
        }
        RemoveFinishedPluginNotes(segmentStart + frames);
    }

    private void RemoveFinishedPluginNotes(long segmentEnd)
    {
        int write = 0;
        for (int i = 0; i < _pluginNoteCount; i++)
        {
            if (_pluginNotes[i].End > segmentEnd)
            {
                _pluginNotes[write++] = _pluginNotes[i];
            }
        }
        _pluginNoteCount = write;
    }

    /// <summary>CLAP 要求事件按帧升序：on 序与 off 序各自有序，插入排序归并（每块事件数小）。</summary>
    private static void SortPluginEvents(PluginEvent[] events, int count)
    {
        for (int i = 1; i < count; i++)
        {
            PluginEvent key = events[i];
            int j = i - 1;
            while (j >= 0 && events[j].FrameOffset > key.FrameOffset)
            {
                events[j + 1] = events[j];
                j--;
            }
            events[j + 1] = key;
        }
    }

    private void Activate(in RenderVoice voice, RenderPlan plan)
    {
        int slot = FindFreeSlot();
        if (slot >= 0)
        {
            InitSlot(slot, voice, plan);
            return;
        }
        // 池满：标记最早结束者为偷（淡出中不可再偷）；新事件进 pending 等淡出释放的槽位
        StealOldest();
        if (_pendingCount >= PendingCapacity)
        {
            DroppedEvents++;
            return;
        }
        _pending[(_pendingHead + _pendingCount) % PendingCapacity] = voice;
        _pendingCount++;
    }

    /// <summary>激活时把 plan 局部索引解析为直接引用——池声部自此与新 plan 解耦（RCU 安全）。</summary>
    private void InitSlot(int slot, in RenderVoice voice, RenderPlan plan)
    {
        _pool[slot] = new Voice
        {
            Active = true,
            Wait = Math.Max(0, voice.StartSample - _position),
            Remaining = voice.LengthSamples,
            Phase = 0,
            PhaseDelta = voice.Kind == RenderVoiceKind.Sine && plan.SampleRate > 0
                ? TwoPi * voice.Frequency / plan.SampleRate
                : 0,
            AssetPosition = voice.AssetOffsetFrames,
            Asset = voice.AssetIndex >= 0 ? plan.Assets[voice.AssetIndex] : null,
            Frequency = voice.Frequency,
            GainLeft = voice.GainLeft,
            GainRight = voice.GainRight,
            Kind = voice.Kind,
            StealFade = 0,
            ChannelIndex = voice.ChannelIndex,
            GainCurve = voice.GainCurveIndex >= 0 ? plan.ChannelGainCurves[voice.GainCurveIndex] : null,
            GainCursor = 0,
        };
    }

    private int FindFreeSlot()
    {
        for (int v = 0; v < _pool.Length; v++)
        {
            if (!_pool[v].Active)
            {
                return v;
            }
        }
        return -1;
    }

    /// <summary>偷「最早结束」的声部（等待+剩余最小，平局取低索引）——确定性。</summary>
    private int StealOldest()
    {
        int victim = -1;
        long bestEnd = long.MaxValue;
        for (int v = 0; v < _pool.Length; v++)
        {
            ref Voice voice = ref _pool[v];
            if (!voice.Active || voice.StealFade > 0)
            {
                continue;
            }
            long endAt = voice.Wait + voice.Remaining;
            if (endAt < bestEnd)
            {
                bestEnd = endAt;
                victim = v;
            }
        }
        if (victim >= 0)
        {
            _pool[victim].StealFade = FadeTail;
        }
        return victim;
    }

    private void FillSlotsFromPending(RenderPlan plan)
    {
        while (_pendingCount > 0)
        {
            int slot = FindFreeSlot();
            if (slot < 0)
            {
                return;
            }
            RenderVoice next = _pending[_pendingHead];
            _pendingHead = (_pendingHead + 1) % PendingCapacity;
            _pendingCount--;
            InitSlot(slot, next, plan);
        }
    }

    private static void RenderGate(ref Voice voice, long segmentStart, Span<float> interleaved, int frames)
    {
        int i = SkipWait(ref voice, frames);
        while (i < frames && voice.Remaining > 0)
        {
            float envelope = voice.Remaining <= FadeTail
                ? voice.Remaining / (float)FadeTail
                : 1f;
            ResolveGain(ref voice, segmentStart + i, out float gainLeft, out float gainRight);
            float left;
            float right;
            if (voice.Kind == RenderVoiceKind.Sine)
            {
                float s = MathF.Sin((float)voice.Phase);
                left = s * gainLeft;
                right = s * gainRight;
                AdvancePhase(ref voice);
            }
            else
            {
                PlanAsset asset = voice.Asset!;
                ReadAsset(asset, ref voice, gainLeft, gainRight, out left, out right);
                if (voice.AssetPosition >= asset.Frames)
                {
                    voice.Remaining = 1;
                }
            }
            interleaved[2 * i] += left * envelope;
            interleaved[2 * i + 1] += right * envelope;
            voice.Remaining--;
            i++;
        }
        if (voice.Remaining == 0 && voice.Wait == 0)
        {
            voice.Active = false;
        }
    }

    private static void RenderFadeTail(ref Voice voice, long segmentStart, Span<float> interleaved, int frames)
    {
        int i = SkipWait(ref voice, frames);
        while (i < frames && voice.StealFade > 0)
        {
            float envelope = voice.StealFade / (float)FadeTail;
            ResolveGain(ref voice, segmentStart + i, out float gainLeft, out float gainRight);
            float left;
            float right;
            if (voice.Kind == RenderVoiceKind.Sine)
            {
                float s = MathF.Sin((float)voice.Phase);
                left = s * gainLeft;
                right = s * gainRight;
                AdvancePhase(ref voice);
            }
            else
            {
                ReadAsset(voice.Asset!, ref voice, gainLeft, gainRight, out left, out right);
            }
            interleaved[2 * i] += left * envelope;
            interleaved[2 * i + 1] += right * envelope;
            voice.StealFade--;
            i++;
        }
        if (voice.StealFade == 0)
        {
            voice.Active = false;
        }
    }

    /// <summary>静态声部直接取预乘增益；曲线声部 velocity × 通道断点线性插值（块内逐样本斜坡）。</summary>
    private static void ResolveGain(ref Voice voice, long position,
        out float gainLeft, out float gainRight)
    {
        if (voice.GainCurve is not { } curve)
        {
            gainLeft = voice.GainLeft;
            gainRight = voice.GainRight;
            return;
        }
        AdvanceGainCursor(ref voice.GainCursor, curve, position);
        InterpolateGain(curve, voice.GainCursor, position, out gainLeft, out gainRight);
        gainLeft *= voice.GainLeft;
        gainRight *= voice.GainRight;
    }

    /// <summary>游标推进；位置回退（循环回绕）时二分重定位——断点数组随 plan 预分配，零分配。</summary>
    private static void AdvanceGainCursor(ref int cursor, GainBreakpoint[] curve, long position)
    {
        if (cursor > 0 && curve[cursor].Sample > position)
        {
            int low = 0, high = curve.Length - 1;
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                if (curve[mid].Sample <= position)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }
            cursor = Math.Min(low, curve.Length - 2);
        }
        else
        {
            while (cursor + 2 < curve.Length && curve[cursor + 1].Sample <= position)
            {
                cursor++;
            }
        }
    }

    /// <summary>断点对 [cursor, cursor+1] 线性插值；t 截断到 [0,1]（时间轴末端外不外推）。</summary>
    private static void InterpolateGain(GainBreakpoint[] curve, int cursor, long position,
        out float gainLeft, out float gainRight)
    {
        if (curve.Length == 1)
        {
            gainLeft = curve[0].GainLeft;
            gainRight = curve[0].GainRight;
            return;
        }
        GainBreakpoint from = curve[cursor];
        GainBreakpoint to = curve[cursor + 1];
        long span = to.Sample - from.Sample;
        float t = span <= 0 ? 1f : Math.Clamp((position - from.Sample) / (float)span, 0f, 1f);
        gainLeft = from.GainLeft + (to.GainLeft - from.GainLeft) * t;
        gainRight = from.GainRight + (to.GainRight - from.GainRight) * t;
    }

    private static void ReadAsset(PlanAsset asset, ref Voice voice,
        float gainLeft, float gainRight, out float left, out float right)
    {
        double position = voice.AssetPosition;
        if (asset.Channels == 1)
        {
            float mono = SampleChannel(asset, position, 0);
            left = mono * gainLeft;
            right = mono * gainRight;
        }
        else
        {
            left = SampleChannel(asset, position, 0) * gainLeft;
            right = SampleChannel(asset, position, 1) * gainRight;
        }
        voice.AssetPosition += voice.Frequency;
    }

    private static float SampleChannel(PlanAsset asset, double position, int channel)
    {
        if (position <= 0)
        {
            return asset.Samples[channel];
        }
        if (position >= asset.Frames - 1)
        {
            return asset.Samples[(asset.Frames - 1) * asset.Channels + channel];
        }
        long i0 = (long)position;
        float frac = (float)(position - i0);
        int ch = asset.Channels;
        float s0 = asset.Samples[i0 * ch + channel];
        float s1 = asset.Samples[(i0 + 1) * ch + channel];
        return s0 + (s1 - s0) * frac;
    }

    private static int SkipWait(ref Voice voice, int frames)
    {
        int i = 0;
        while (voice.Wait > 0 && i < frames)
        {
            voice.Wait--;
            i++;
        }
        return i;
    }

    private static void AdvancePhase(ref Voice voice)
    {
        voice.Phase += voice.PhaseDelta;
        if (voice.Phase >= TwoPi)
        {
            voice.Phase -= TwoPi;
        }
    }
}
