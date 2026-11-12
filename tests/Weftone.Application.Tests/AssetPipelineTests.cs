using System.Buffers.Binary;
using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Adapters.Codecs;
using Weftone.Application;
using Weftone.Domain;

namespace Weftone.Application.Tests;

public class AssetPipelineTests
{
    private static byte[] Wav16(int sampleRate, params short[] mono)
    {
        int dataBytes = mono.Length * 2;
        var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], (uint)(36 + dataBytes));
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..20], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..22], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[22..24], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..28], (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..32], (uint)(sampleRate * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..34], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header[34..36], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..44], (uint)dataBytes);
        stream.Write(header);
        byte[] body = new byte[dataBytes];
        for (int i = 0; i < mono.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(i * 2), mono[i]);
        }
        stream.Write(body);
        return stream.ToArray();
    }

    [Test]
    public void DrWav_decodes_pcm16_to_f32()
    {
        using var decoder = new DrWavDecoder();
        DecodedAudio audio = decoder.Decode(new MemoryStream(Wav16(24_000, 16384, -16384, 0, 8192)));
        Assert.Multiple(() =>
        {
            Assert.That(audio.Channels, Is.EqualTo(1));
            Assert.That(audio.SampleRate, Is.EqualTo(24_000));
            Assert.That(audio.Frames, Is.EqualTo(4));
        });
        Assert.That(audio.Interleaved, Is.EqualTo(new float[] { 0.5f, -0.5f, 0f, 0.25f }).Within(1e-6f));
    }

    [Test]
    public void DrWav_rejects_non_wav_bytes()
    {
        using var decoder = new DrWavDecoder();
        Assert.That(() => decoder.Decode(new MemoryStream([1, 2, 3, 4])),
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void Linear_resampler_upsamples_and_is_identity_when_rates_match()
    {
        var resampler = new LinearResampler();
        var source = new DecodedAudio([0f, 1f], 1, 24_000, 2);
        DecodedAudio identity = resampler.Resample(source, 24_000);
        Assert.That(identity, Is.SameAs(source), "同采样率直接返回");

        DecodedAudio up = resampler.Resample(source, 48_000);
        Assert.Multiple(() =>
        {
            Assert.That(up.Frames, Is.EqualTo(4));
            Assert.That(up.SampleRate, Is.EqualTo(48_000));
        });
        Assert.That(up.Interleaved, Is.EqualTo(new float[] { 0f, 0.5f, 1f, 1f }).Within(1e-6f));
    }

    [Test]
    public void AssetPreparer_decodes_resamples_and_maps_ids()
    {
        var document = new ProjectDocument { SampleRate = 48_000 };
        document.Assets.Add(new AssetRef { Id = "a1", Path = "assets/a.wav", Sha256 = "" });
        var preparer = new AssetPreparer();
        byte[] wav = Wav16(24_000, 8192, 8192);

        List<PreparedAsset> prepared = preparer.Prepare(
            document,
            _ => new MemoryStream(wav),
            new DrWavDecoder(),
            new LinearResampler());

        Assert.That(prepared.Count, Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(prepared[0].AssetId, Is.EqualTo("a1"));
            Assert.That(prepared[0].Channels, Is.EqualTo(1));
            Assert.That(prepared[0].Frames, Is.EqualTo(4), "24k→48k 上采样翻倍");
        });
    }
}
