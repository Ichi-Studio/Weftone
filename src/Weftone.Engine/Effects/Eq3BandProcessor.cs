namespace Weftone.Engine.Effects;

/// <summary>内置效果处理器（SPEC M5）：音频线程调用——实现必须零分配，
/// 状态随实例持有；输入输出为交错立体声（就地处理）。</summary>
public interface IEffectProcessor
{
    void Process(Span<float> interleaved, int frames);
}

/// <summary>
/// 三段均衡（SPEC M5 D1=a）：低架 200Hz / 中峰 1kHz / 高架 4kHz，
/// RBJ biquad，L/R 独立状态；参数 = [低增益 dB, 中增益 dB, 高增益 dB]。
/// 确定性：纯浮点递归，同输入同参数位级一致。
/// </summary>
public sealed class Eq3BandProcessor : IEffectProcessor
{
    private struct Biquad
    {
        public double B0, B1, B2, A1, A2;
        public double X1, X2, Y1, Y2;
    }

    private readonly Biquad[] _filters = new Biquad[6]; // 3 段 × L/R

    public Eq3BandProcessor(double lowGainDb, double midGainDb, double highGainDb)
    {
        DesignShelf(ref _filters[0], 200.0, lowGainDb, lowShelf: true);
        DesignShelf(ref _filters[1], 200.0, lowGainDb, lowShelf: true);
        DesignPeak(ref _filters[2], 1000.0, midGainDb);
        DesignPeak(ref _filters[3], 1000.0, midGainDb);
        DesignShelf(ref _filters[4], 4000.0, highGainDb, lowShelf: false);
        DesignShelf(ref _filters[5], 4000.0, highGainDb, lowShelf: false);
    }

    public void Process(Span<float> interleaved, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            double left = interleaved[2 * i];
            double right = interleaved[2 * i + 1];
            left = Run(ref _filters[0], left);
            right = Run(ref _filters[1], right);
            left = Run(ref _filters[2], left);
            right = Run(ref _filters[3], right);
            left = Run(ref _filters[4], left);
            right = Run(ref _filters[5], right);
            interleaved[2 * i] = (float)left;
            interleaved[2 * i + 1] = (float)right;
        }
    }

    private static double Run(ref Biquad f, double x)
    {
        double y = f.B0 * x + f.B1 * f.X1 + f.B2 * f.X2 - f.A1 * f.Y1 - f.A2 * f.Y2;
        f.X2 = f.X1;
        f.X1 = x;
        f.Y2 = f.Y1;
        f.Y1 = y;
        return y;
    }

    private static void DesignShelf(ref Biquad f, double freqHz, double gainDb, bool lowShelf)
    {
        // RBJ audio-eq-cookbook（采样率 48000，Q = 1/√2）
        const double sampleRate = 48_000.0;
        double a = Math.Pow(10.0, gainDb / 40.0);
        double w0 = 2.0 * Math.PI * freqHz / sampleRate;
        double cw = Math.Cos(w0);
        double sw = Math.Sin(w0);
        double alpha = sw / Math.Sqrt(2.0);
        double twoSqrtAAlpha = 2.0 * Math.Sqrt(a) * alpha;
        double b0, b1, b2, a0, a1, a2;
        if (lowShelf)
        {
            b0 = a * ((a + 1) - (a - 1) * cw + twoSqrtAAlpha);
            b1 = 2.0 * a * ((a - 1) - (a + 1) * cw);
            b2 = a * ((a + 1) - (a - 1) * cw - twoSqrtAAlpha);
            a0 = (a + 1) + (a - 1) * cw + twoSqrtAAlpha;
            a1 = -2.0 * ((a - 1) + (a + 1) * cw);
            a2 = (a + 1) + (a - 1) * cw - twoSqrtAAlpha;
        }
        else
        {
            b0 = a * ((a + 1) + (a - 1) * cw + twoSqrtAAlpha);
            b1 = -2.0 * a * ((a - 1) + (a + 1) * cw);
            b2 = a * ((a + 1) + (a - 1) * cw - twoSqrtAAlpha);
            a0 = (a + 1) - (a - 1) * cw + twoSqrtAAlpha;
            a1 = 2.0 * ((a - 1) - (a + 1) * cw);
            a2 = (a + 1) - (a - 1) * cw - twoSqrtAAlpha;
        }
        Normalize(ref f, b0, b1, b2, a0, a1, a2);
    }

    private static void DesignPeak(ref Biquad f, double freqHz, double gainDb)
    {
        const double sampleRate = 48_000.0;
        double a = Math.Pow(10.0, gainDb / 40.0);
        double w0 = 2.0 * Math.PI * freqHz / sampleRate;
        double cw = Math.Cos(w0);
        double sw = Math.Sin(w0);
        double alpha = sw / Math.Sqrt(2.0);
        double b0 = 1.0 + alpha * a;
        double b1 = -2.0 * cw;
        double b2 = 1.0 - alpha * a;
        double a0 = 1.0 + alpha / a;
        double a1 = -2.0 * cw;
        double a2 = 1.0 - alpha / a;
        Normalize(ref f, b0, b1, b2, a0, a1, a2);
    }

    private static void Normalize(ref Biquad f, double b0, double b1, double b2,
        double a0, double a1, double a2)
    {
        f.B0 = b0 / a0;
        f.B1 = b1 / a0;
        f.B2 = b2 / a0;
        f.A1 = a1 / a0;
        f.A2 = a2 / a0;
    }
}

/// <summary>效果处理器工厂：plan 节点 Kind → 实例（非 RT 调用）。</summary>
public static class EffectProcessorFactory
{
    /// <summary>未知类型返回 null（节点跳过——前向兼容）。</summary>
    public static IEffectProcessor? Create(string kind, double[] parameters)
    {
        if (kind == "Eq3Band")
        {
            double low = parameters.Length > 0 ? parameters[0] : 0;
            double mid = parameters.Length > 1 ? parameters[1] : 0;
            double high = parameters.Length > 2 ? parameters[2] : 0;
            return new Eq3BandProcessor(low, mid, high);
        }
        return null;
    }
}
