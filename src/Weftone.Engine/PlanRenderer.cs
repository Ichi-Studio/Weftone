using Weftone.Abstractions.Rendering;

namespace Weftone.Engine;

/// <summary>
/// RenderPlan 离线/实时两用渲染器：64 复音预分配池，事件按 StartSample 顺序激活，
/// 池满时最早结束者被偷（FadeTail 线性淡出防 click），新事件入 pending 环等位。
/// Sine：双精度相位；Sampler：资产线性插值、MIDI60=原速、资产耗尽自然结束。
/// 全程零分配（RtAssert 守护）。
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
        public double AssetPosition;
        public int AssetIndex;
        public float Frequency;
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
                RenderFadeTail(ref voice, plan.Assets, interleaved, frames);
            }
            else
            {
                RenderGate(ref voice, plan.Assets, interleaved, frames);
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
            AssetPosition = 0,
            AssetIndex = voice.AssetIndex,
            Frequency = voice.Frequency,
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

    private static void RenderGate(ref Voice voice, PlanAsset[] assets, Span<float> interleaved, int frames)
    {
        int i = SkipWait(ref voice, frames);
        while (i < frames && voice.Remaining > 0)
        {
            float envelope = voice.Remaining <= FadeTail
                ? voice.Remaining / (float)FadeTail
                : 1f;
            float left;
            float right;
            if (voice.Kind == RenderVoiceKind.Sine)
            {
                float s = MathF.Sin((float)voice.Phase);
                left = s * voice.GainLeft;
                right = s * voice.GainRight;
                AdvancePhase(ref voice);
            }
            else
            {
                PlanAsset asset = assets[voice.AssetIndex];
                ReadAsset(asset, ref voice, voice.GainLeft, voice.GainRight, out left, out right);
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

    private static void RenderFadeTail(ref Voice voice, PlanAsset[] assets, Span<float> interleaved, int frames)
    {
        int i = SkipWait(ref voice, frames);
        while (i < frames && voice.StealFade > 0)
        {
            float envelope = voice.StealFade / (float)FadeTail;
            float left;
            float right;
            if (voice.Kind == RenderVoiceKind.Sine)
            {
                float s = MathF.Sin((float)voice.Phase);
                left = s * voice.GainLeft;
                right = s * voice.GainRight;
                AdvancePhase(ref voice);
            }
            else
            {
                ReadAsset(assets[voice.AssetIndex], ref voice, voice.GainLeft, voice.GainRight, out left, out right);
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
