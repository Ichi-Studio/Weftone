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
/// </summary>
public sealed class PlanRenderer : IPlanRenderer
{
    private const int MaxVoices = 64;
    private const int FadeTail = 128;
    private const int PendingCapacity = 32;
    private const double TwoPi = 6.283185307179586476925286766559;

    private struct Voice
    {
        public bool Active;
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
    private int _pendingHead;
    private int _pendingCount;
    private volatile RenderPlan? _plan;
    /// <summary>游标/循环状态所属 plan（音频线程内与 _plan 快照比对，检测交换后重定位）。</summary>
    private RenderPlan? _cursorPlan;
    private int _nextEvent;
    private long _position;
    private long _loopStart;
    private long _loopEnd;
    private int _masterGainCursor;

    public long Position => _position;

    public long DroppedEvents { get; private set; }

    public void Load(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plan = plan;
        _position = 0;
        DroppedEvents = 0;
        Array.Clear(_pool);
        ResyncCursor(plan);
    }

    /// <summary>RCU 交换：仅 volatile 写入；重定位在音频线程 Render 内惰性完成（SPEC M2 竖切 4）。</summary>
    public void SwapPlan(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plan = plan;
    }

    public void Render(Span<float> interleaved)
    {
        interleaved.Clear();
        RenderPlan? plan = _plan;
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
                    // 回绕：位置回 loopStart，事件游标归零重触发；活跃声部自然延续尾音
                    _position = _loopStart;
                    _nextEvent = 0;
                    tillEnd = _loopEnd - _position;
                }
                if (tillEnd > 0 && tillEnd < segment)
                {
                    segment = tillEnd;
                }
            }
            RenderSegment(plan, interleaved, rendered, (int)segment);
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
    /// 放弃旧 plan 的 pending 溢出事件、更新循环区间；仅音频线程调用。</summary>
    private void ResyncCursor(RenderPlan plan)
    {
        _cursorPlan = plan;
        DroppedEvents += _pendingCount;
        _pendingHead = 0;
        _pendingCount = 0;
        _loopStart = plan.LoopStartSample;
        _loopEnd = plan.LoopEndSample;
        if (_masterGainCursor >= plan.MasterGainCurve?.Length)
        {
            _masterGainCursor = 0;
        }
        RenderVoice[] voices = plan.Voices;
        int low = 0, high = voices.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (voices[mid].StartSample < _position)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        _nextEvent = low;
    }

    private void RenderSegment(RenderPlan plan, Span<float> interleaved, int offsetFrames, int frames)
    {
        Span<float> slice = interleaved.Slice(offsetFrames * 2, frames * 2);
        long segmentStart = _position;
        long end = segmentStart + frames;

        while (_nextEvent < plan.Voices.Length && plan.Voices[_nextEvent].StartSample < end)
        {
            Activate(plan.Voices[_nextEvent], plan);
            _nextEvent++;
        }

        for (int v = 0; v < _pool.Length; v++)
        {
            ref Voice voice = ref _pool[v];
            if (!voice.Active)
            {
                continue;
            }
            if (voice.StealFade > 0)
            {
                RenderFadeTail(ref voice, segmentStart, slice, frames);
            }
            else
            {
                RenderGate(ref voice, segmentStart, slice, frames);
            }
        }

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
            AssetPosition = 0,
            Asset = voice.AssetIndex >= 0 ? plan.Assets[voice.AssetIndex] : null,
            Frequency = voice.Frequency,
            GainLeft = voice.GainLeft,
            GainRight = voice.GainRight,
            Kind = voice.Kind,
            StealFade = 0,
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
