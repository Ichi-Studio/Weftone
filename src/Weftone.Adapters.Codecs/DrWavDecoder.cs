using Weftone.Abstractions;

namespace Weftone.Adapters.Codecs;

/// <summary>dr_wav 解码适配器：支持 16/24/32f/32i PCM WAV → f32 交错。整文件解码，非 RT。</summary>
public sealed unsafe class DrWavDecoder : ISampleDecoder
{
    public DecodedAudio Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        NativeProbe.EnsureInstalled();

        using var managed = new MemoryStream();
        stream.CopyTo(managed);
        byte[] bytes = managed.ToArray();
        if (bytes.Length == 0)
        {
            throw new InvalidDataException("空音频流");
        }

        fixed (byte* data = bytes)
        {
            int rc = WeftCodecsNative.weft_wav_decode(data, (nuint)bytes.Length,
                out float* samples, out uint channels, out uint sampleRate, out ulong frames);
            if (rc != 0)
            {
                throw new InvalidDataException($"WAV 解码失败 rc={rc}（-2 非法 WAV / -3 空或超限 / -5 截断）");
            }
            try
            {
                int channelCount = checked((int)channels);
                long frameCount = checked((long)frames);
                var interleaved = new float[frameCount * channelCount];
                fixed (float* target = interleaved)
                {
                    Buffer.MemoryCopy(samples, target, interleaved.Length * sizeof(float), interleaved.Length * sizeof(float));
                }
                return new DecodedAudio(interleaved, channelCount, checked((int)sampleRate), frameCount);
            }
            finally
            {
                WeftCodecsNative.weft_wav_free(samples);
            }
        }
    }

    public void Dispose()
    {
    }
}
