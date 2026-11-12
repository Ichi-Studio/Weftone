namespace Weftone.Abstractions;

/// <summary>解码结果：f32 交错 PCM。</summary>
public sealed record DecodedAudio(float[] Interleaved, int Channels, int SampleRate, long Frames);

/// <summary>采样解码端口（ADR-0004：解码用轮子，实现于 adapter）。</summary>
public interface ISampleDecoder : IDisposable
{
    DecodedAudio Decode(Stream stream);
}

/// <summary>重采样端口；M1a 为离线线性插值（拍板 D3），高质量实现后续可替换。</summary>
public interface IResampler
{
    DecodedAudio Resample(DecodedAudio source, int targetSampleRate);
}
