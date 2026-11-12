using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Weftone.Tools.HeadlessRender;

/// <summary>WAVE_FORMAT_IEEE_FLOAT（32-bit 小端）WAV 写出：先写头，再流式追加样本。</summary>
public static class WavWriter
{
    public static void WriteHeader(Stream stream, int sampleRate, int channels, long totalSamples)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalSamples, 0);
        long dataBytes = totalSamples * sizeof(float);
        long riffSize = 36 + dataBytes;
        if (riffSize > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(totalSamples), "M0 单 WAV 上限 4GB");
        }

        Span<byte> header = stackalloc byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], (uint)riffSize);
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..20], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..22], 3);
        BinaryPrimitives.WriteUInt16LittleEndian(header[22..24], (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..28], (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..32], (uint)(sampleRate * channels * sizeof(float)));
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..34], (ushort)(channels * sizeof(float)));
        BinaryPrimitives.WriteUInt16LittleEndian(header[34..36], 32);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..44], (uint)dataBytes);

        stream.Write(header);
    }

    public static void WriteSamples(Stream stream, ReadOnlySpan<float> interleaved)
        => stream.Write(MemoryMarshal.AsBytes(interleaved));
}
