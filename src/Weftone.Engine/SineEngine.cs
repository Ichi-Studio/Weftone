using Weftone.Abstractions;

namespace Weftone.Engine;

public sealed class SineEngine
{
    private const double TwoPi = 6.283185307179586476925286766559;

    private readonly double _phaseDelta;
    private readonly AudioRenderCallback _callback;
    private double _phase;

    public SineEngine(float frequency, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        if (float.IsNaN(frequency) || frequency <= 0f || frequency >= sampleRate / 2f)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "频率须在 (0, sampleRate/2)");
        }
        _phaseDelta = TwoPi * frequency / sampleRate;
        _callback = Render;
    }

    /// <summary>预捕获的回调委托，调用方持有它可避免音频路径上的方法组转换分配。</summary>
    public AudioRenderCallback Callback => _callback;

    public void Render(Span<float> interleavedOutput)
    {
        for (int i = 0; i < interleavedOutput.Length; i++)
        {
            interleavedOutput[i] = MathF.Sin((float)_phase);
            _phase += _phaseDelta;
            if (_phase >= TwoPi)
            {
                _phase -= TwoPi;
            }
        }
    }
}
