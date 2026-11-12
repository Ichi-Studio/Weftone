using Weftone.Domain;

namespace Weftone.Application;

/// <summary>tick → sample 的整数映射（M1a 拍板 D1）：截断除法，确定性。</summary>
public static class TickMath
{
    public static (long Numerator, long Denominator) SamplesPerTick(int sampleRate, int tempo)
        => (sampleRate * 60L, (long)tempo * TimeBase.Ppq);

    public static long TicksToSamples(long ticks, long numerator, long denominator)
        => denominator == 0 ? 0 : ticks * numerator / denominator;
}
