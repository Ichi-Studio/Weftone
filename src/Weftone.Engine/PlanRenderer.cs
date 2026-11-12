using Weftone.Abstractions.Rendering;

namespace Weftone.Engine;

/// <summary>
/// RenderPlan 离线/实时两用渲染器：64 复音预分配池，事件按 StartSample 顺序激活，
/// 池满时最早结束者被偷（FadeTail 线性淡出防 click），新事件入 pending 环等位。
/// 全程零分配（RtAssert 守护）。M1a 竖切 2：仅渲染 Sine 声部。
/// </summary>
public sealed class PlanRenderer
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
        public float GainLeft;
        public float GainRight;
        public RenderVoiceKind Kind;
        public int StealFade;
    }

    private readonly Voice[] _pool = new Voice[MaxVoices];
    private readonly RenderVoice[] _pending = new RenderVoice[PendingCapacity];
    private int _pendingHead;
    private int _pendingCount;
    private RenderPlan? _plan;
    private int _nextEvent;
    private long _position;

    public long Position => _position;

    public long DroppedEvents { get; private set; }

    public void Load(RenderPlan plan)
    {
        _plan = plan;
        _position = 0;
        _nextEvent = 0;
        _pendingHead = 0;
        _pendingCount = 0;
        DroppedEvents = 0;
        Array.Clear(_pool);
    }

    public void Render(Span<float> interleaved)
    {
        interleaved.Clear();
        RenderPlan? plan = _plan;
        if (plan is null)
        {
            return;
        }
        int frames = interleaved.Length / 2;
        if (frames == 0)
        {
            return;
        }
        long end = _position + frames;

        while (_nextEvent < plan.Voices.Length && plan.Voices[_nextEvent].StartSample < end)
        {
            Activate(plan.Voices[_nextEvent], plan.SampleRate);
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
                RenderFadeTail(ref voice, interleaved, frames);
            }
            else
            {
                RenderGate(ref voice, interleaved, frames);
            }
        }

        FillSlotsFromPending(plan.SampleRate);

        if (plan.MasterGain != 1f)
        {
            float master = plan.MasterGain;
            for (int i = 0; i < interleaved.Length; i++)
            {
                interleaved[i] *= master;
            }
        }
        _position = end;
    }

    private void Activate(in RenderVoice voice, int sampleRate)
    {
        int slot = FindFreeSlot();
        if (slot >= 0)
        {
            InitSlot(slot, voice, sampleRate);
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

    private void InitSlot(int slot, in RenderVoice voice, int sampleRate)
    {
        _pool[slot] = new Voice
        {
            Active = true,
            Wait = Math.Max(0, voice.StartSample - _position),
            Remaining = voice.LengthSamples,
            Phase = 0,
            PhaseDelta = voice.Kind == RenderVoiceKind.Sine && sampleRate > 0
                ? TwoPi * voice.Frequency / sampleRate
                : 0,
            GainLeft = voice.GainLeft,
            GainRight = voice.GainRight,
            Kind = voice.Kind,
            StealFade = 0,
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

    private void FillSlotsFromPending(int sampleRate)
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
            InitSlot(slot, next, sampleRate);
        }
    }

    private static void RenderGate(ref Voice voice, Span<float> interleaved, int frames)
    {
        int i = SkipWait(ref voice, frames);
        while (i < frames && voice.Remaining > 0)
        {
            float envelope = voice.Remaining <= FadeTail
                ? voice.Remaining / (float)FadeTail
                : 1f;
            float sample = voice.Kind == RenderVoiceKind.Sine
                ? MathF.Sin((float)voice.Phase)
                : 0f;
            interleaved[2 * i] += sample * voice.GainLeft * envelope;
            interleaved[2 * i + 1] += sample * voice.GainRight * envelope;
            AdvancePhase(ref voice);
            voice.Remaining--;
            i++;
        }
        if (voice.Remaining == 0 && voice.Wait == 0)
        {
            voice.Active = false;
        }
    }

    private static void RenderFadeTail(ref Voice voice, Span<float> interleaved, int frames)
    {
        int i = SkipWait(ref voice, frames);
        while (i < frames && voice.StealFade > 0)
        {
            float envelope = voice.StealFade / (float)FadeTail;
            float sample = voice.Kind == RenderVoiceKind.Sine
                ? MathF.Sin((float)voice.Phase)
                : 0f;
            interleaved[2 * i] += sample * voice.GainLeft * envelope;
            interleaved[2 * i + 1] += sample * voice.GainRight * envelope;
            AdvancePhase(ref voice);
            voice.StealFade--;
            i++;
        }
        if (voice.StealFade == 0)
        {
            voice.Active = false;
        }
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
