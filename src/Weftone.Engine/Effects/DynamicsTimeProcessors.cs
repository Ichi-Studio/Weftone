namespace Weftone.Engine.Effects;

/// <summary>
/// 压缩器（SPEC M5 D1）：feed-forward 峰值检测 + 包络 + 增益，
/// L/R 链接（立体声同增益防漂移）；参数 [threshold dB, ratio, attack ms, release ms]。
/// </summary>
public sealed class CompressorProcessor : IEffectProcessor
{
    private double _thresholdDb;
    private double _ratio;
    private double _attackCoeff;
    private double _releaseCoeff;
    private double _envelope;

    public CompressorProcessor(double thresholdDb, double ratio, double attackMs, double releaseMs)
    {
        SetParameters([thresholdDb, ratio, attackMs, releaseMs]);
    }

    public void SetParameters(double[] parameters)
    {
        _thresholdDb = parameters.Length > 0 ? parameters[0] : -24;
        _ratio = Math.Clamp(parameters.Length > 1 ? parameters[1] : 4, 1.0, 20.0);
        double attackMs = parameters.Length > 2 ? parameters[2] : 10;
        double releaseMs = parameters.Length > 3 ? parameters[3] : 100;
        _attackCoeff = attackMs <= 0 ? 0 : Math.Exp(-1.0 / (attackMs * 48.0));
        _releaseCoeff = releaseMs <= 0 ? 0 : Math.Exp(-1.0 / (releaseMs * 48.0));
    }

    public void Process(Span<float> interleaved, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            float left = interleaved[2 * i];
            float right = interleaved[2 * i + 1];
            double peak = Math.Max(Math.Abs(left), Math.Abs(right));
            // 包络：attack 快升 / release 慢降
            double coeff = peak > _envelope ? _attackCoeff : _releaseCoeff;
            _envelope = coeff * _envelope + (1.0 - coeff) * peak;
            double db = _envelope > 1e-9 ? 20.0 * Math.Log10(_envelope) : -200.0;
            double reductionDb = 0;
            if (db > _thresholdDb)
            {
                reductionDb = (db - _thresholdDb) * (1.0 - 1.0 / _ratio);
            }
            double gain = Math.Pow(10.0, -reductionDb / 20.0);
            interleaved[2 * i] = (float)(left * gain);
            interleaved[2 * i + 1] = (float)(right * gain);
        }
    }
}

/// <summary>
/// 延迟（SPEC M5 D1）：立体声反馈延迟线；参数 [timeMs, feedback 0..1, mix 0..1]。
/// 预分配 2 秒延迟线，零分配。
/// </summary>
public sealed class DelayProcessor : IEffectProcessor
{
    private const int MaxSamples = 96_000; // 2s @48k
    private readonly double[] _lines;
    private double _feedback;
    private double _mix;
    private int _position;

    public DelayProcessor(double timeMs, double feedback, double mix)
    {
        int delaySamples = Math.Clamp((int)(timeMs * 48.0), 1, MaxSamples - 1);
        _lines = new double[delaySamples * 2]; // 环长 = 延迟长度（写前读 → 恰好延迟 N 样本）
        SetParameters([timeMs, feedback, mix]);
    }

    public void SetParameters(double[] parameters)
    {
        // 时间长度构造期固定（缓冲不可变）；反馈/混合即时生效
        _feedback = Math.Clamp(parameters.Length > 1 ? parameters[1] : 0.3, 0, 0.95);
        _mix = Math.Clamp(parameters.Length > 2 ? parameters[2] : 0.3, 0, 1);
    }

    public void Process(Span<float> interleaved, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            int readL = (_position * 2) % _lines.Length;
            double delayedL = _lines[readL];
            double delayedR = _lines[readL + 1];
            double dryL = interleaved[2 * i];
            double dryR = interleaved[2 * i + 1];
            interleaved[2 * i] = (float)(dryL + delayedL * _mix);
            interleaved[2 * i + 1] = (float)(dryR + delayedR * _mix);
            _lines[readL] = (dryL + delayedL * _feedback) * 0.7071; // 防反馈爆音
            _lines[readL + 1] = (dryR + delayedR * _feedback) * 0.7071;
            _position = (_position + 1) % (_lines.Length / 2);
        }
    }
}

/// <summary>
/// FDN 混响（SPEC M5 D5=a）：4×4 Hadamard 反馈延迟网络 + 预分配延迟线；
/// 参数 [decaySeconds, size 0..1, preDelayMs]。
/// </summary>
public sealed class ReverbProcessor : IEffectProcessor
{
    private const int Lines = 4;
    private readonly double[][] _delays = new double[Lines][];
    private readonly int[] _lengths = new int[Lines];
    private readonly int[] _positions = new int[Lines];
    private readonly double[] _buffer = new double[Lines];
    private readonly double[] _output = new double[Lines];
    private readonly int _preDelay;
    private readonly double[] _preDelayBuffer;
    private int _preDelayPosition;
    private double _feedback;

    private static readonly int[] BaseLengths = [1423, 1913, 2371, 2917]; // 互质延迟（无金属感）
    private double _lengthsScale;

    public void SetParameters(double[] parameters)
    {
        // 衰减时间即时生效（改反馈系数）；尺寸/预延迟构造期固定（缓冲不可变）
        double decaySeconds = parameters.Length > 0 ? parameters[0] : 1.5;
        double feedback = decaySeconds <= 0 ? 0
            : Math.Pow(0.001, BaseLengths[0] * _lengthsScale / (decaySeconds * 48_000.0));
        _feedback = Math.Clamp(feedback, 0, 0.98);
    }

    public ReverbProcessor(double decaySeconds, double size, double preDelayMs)
    {
        double scale = 0.6 + Math.Clamp(size, 0, 1) * 0.8;
        double feedback = decaySeconds <= 0 ? 0 : Math.Pow(0.001, BaseLengths[0] * scale / (decaySeconds * 48_000.0));
        _feedback = Math.Clamp(feedback, 0, 0.98);
        _lengthsScale = scale;
        _preDelay = Math.Clamp((int)(Math.Clamp(preDelayMs, 0, 200) * 48.0), 0, 9600);
        _preDelayBuffer = new double[Math.Max(1, _preDelay) * 2];
        for (int i = 0; i < Lines; i++)
        {
            _lengths[i] = (int)(BaseLengths[i] * scale);
            _delays[i] = new double[_lengths[i]];
            _positions[i] = 0;
        }
    }

    public void Process(Span<float> interleaved, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            double dryL = interleaved[2 * i];
            double dryR = interleaved[2 * i + 1];
            // 预延迟
            int preIndex = (_preDelayPosition * 2) % _preDelayBuffer.Length;
            double preL = _preDelayBuffer[preIndex];
            double preR = _preDelayBuffer[preIndex + 1];
            _preDelayBuffer[preIndex] = dryL;
            _preDelayBuffer[preIndex + 1] = dryR;
            _preDelayPosition = (_preDelayPosition + 1) % Math.Max(1, _preDelay);
            if (_preDelay == 0)
            {
                preL = dryL;
                preR = dryR;
            }
            // 输入注入
            double input = (preL + preR) * 0.5;
            // 读取各延迟线尾端
            for (int l = 0; l < Lines; l++)
            {
                _buffer[l] = _delays[l][_positions[l]];
            }
            // Hadamard 4×4 混合 + 反馈
            for (int l = 0; l < Lines; l++)
            {
                double mixed = input;
                for (int k = 0; k < Lines; k++)
                {
                    mixed += _feedback * Hadamard(l, k) * _buffer[k] * 0.5;
                }
                _delays[l][_positions[l]] = mixed;
                _output[l] = _buffer[l];
                _positions[l] = (_positions[l] + 1) % _lengths[l];
            }
            // 输出（湿 0.35 混入）
            double wetL = (_output[0] + _output[2]) * 0.35;
            double wetR = (_output[1] + _output[3]) * 0.35;
            interleaved[2 * i] = (float)(dryL + wetL);
            interleaved[2 * i + 1] = (float)(dryR + wetR);
        }
    }

    private static double Hadamard(int row, int column)
    {
        return ((row & column) == 0 ? 1 : -1) / 2.0;
    }
}
