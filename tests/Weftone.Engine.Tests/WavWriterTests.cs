using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using Weftone.Tools.HeadlessRender;

namespace Weftone.Engine.Tests;

public class WavWriterTests
{
    [Test]
    public void Header_fields_and_sample_roundtrip()
    {
        float[] samples = Enumerable.Range(0, 4_800).Select(i => MathF.Sin(i * 0.01f)).ToArray();
        using var stream = new MemoryStream();
        WavWriter.WriteHeader(stream, 48_000, 2, samples.Length);
        WavWriter.WriteSamples(stream, samples);

        stream.Position = 0;
        byte[] all = stream.ToArray();
        Assert.That(all.Length, Is.EqualTo(44 + samples.Length * 4));
        Assert.That(Encoding.ASCII.GetString(all, 0, 4), Is.EqualTo("RIFF"));
        Assert.That(Encoding.ASCII.GetString(all, 8, 4), Is.EqualTo("WAVE"));
        Assert.That(Encoding.ASCII.GetString(all, 12, 4), Is.EqualTo("fmt "));
        Assert.That(Encoding.ASCII.GetString(all, 36, 4), Is.EqualTo("data"));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(4)), Is.EqualTo((uint)(all.Length - 8)));

        Assert.Multiple(() =>
        {
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(all.AsSpan(20)), Is.EqualTo(3), "format=IEEE float");
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(all.AsSpan(22)), Is.EqualTo((ushort)2), "channels");
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(24)), Is.EqualTo(48_000u), "sampleRate");
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(28)), Is.EqualTo(48_000u * 2 * 4), "byteRate");
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(all.AsSpan(32)), Is.EqualTo((ushort)8), "blockAlign");
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(all.AsSpan(34)), Is.EqualTo((ushort)32), "bits");
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(40)), Is.EqualTo((uint)(samples.Length * 4)), "dataSize");
        });

        float[] back = MemoryMarshal.Cast<byte, float>(all.AsSpan(44)).ToArray();
        Assert.That(back, Is.EqualTo(samples));
    }
}
