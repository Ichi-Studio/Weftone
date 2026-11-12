using Weftone.Abstractions;

namespace Weftone.Adapters.Codecs;

/// <summary>
/// 离线线性插值重采样（M1a 拍板 D3）：逐声道插值，确定性；
/// 高质量实现（r8brain）留 M4 经 IResampler 替换。
/// </summary>
public sealed class LinearResampler : IResampler
{
    public DecodedAudio Resample(DecodedAudio source, int targetSampleRate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetSampleRate, 1);
        if (source.SampleRate == targetSampleRate)
        {
            return source;
        }

        double ratio = (double)targetSampleRate / source.SampleRate;
        long outFrames = (long)Math.Floor(source.Frames * ratio);
        if (outFrames < 1)
        {
            outFrames = 1;
        }
        int channels = source.Channels;
        var output = new float[outFrames * channels];

        for (long frame = 0; frame < outFrames; frame++)
        {
            double sourcePos = frame / ratio;
            long i0 = (long)sourcePos;
            long i1 = Math.Min(i0 + 1, source.Frames - 1);
            float frac = (float)(sourcePos - i0);
            for (int ch = 0; ch < channels; ch++)
            {
                float s0 = source.Interleaved[i0 * channels + ch];
                float s1 = source.Interleaved[i1 * channels + ch];
                output[frame * channels + ch] = s0 + (s1 - s0) * frac;
            }
        }
        return new DecodedAudio(output, channels, targetSampleRate, outFrames);
    }
}
