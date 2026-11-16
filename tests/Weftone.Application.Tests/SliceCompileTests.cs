using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Application;
using Weftone.Domain;

namespace Weftone.Application.Tests;

/// <summary>SPEC M4 竖切 2：切片演奏映射——key=60+i → 第 i 切片（资产内偏移），
/// 越界切片优雅跳过；非切片通道行为不变。</summary>
public class SliceCompileTests
{
    private static ProjectDocument NewSliceDocument()
    {
        var document = new ProjectDocument { Tempo = 120, SampleRate = 48_000 };
        document.Assets.Add(new AssetRef { Id = "a", Path = "assets/a.wav" });
        document.Channels.Add(new Channel
        {
            Id = 1, Kind = ChannelKind.Sampler, AssetId = "a", SliceMode = true,
        });
        var pattern = new Pattern { Id = 10 };
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 60, Velocity = 1, LengthTicks = 96 });
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 192, Key = 61, Velocity = 1, LengthTicks = 96 });
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 384, Key = 62, Velocity = 1, LengthTicks = 96 });
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        document.Assets[0].SlicePoints.AddRange([0, 800, 1600]);
        return document;
    }

    private sealed class FixedDecoder : ISampleDecoder
    {
        public DecodedAudio Decode(Stream stream) =>
            new([0.1f, -0.2f, 0.3f, -0.4f], 1, 48_000, 4);
        public void Dispose() { }
    }

    private sealed class IdentityResampler : IResampler
    {
        public DecodedAudio Resample(DecodedAudio input, int targetSampleRate) => input;
    }

    private static RenderPlan Compile(ProjectDocument document)
    {
        List<PreparedAsset> prepared = new AssetPreparer().Prepare(
            document, _ => new MemoryStream([0]), new FixedDecoder(), new IdentityResampler());
        return new DocumentCompiler().Compile(document, prepared);
    }

    [Test]
    public void Key_maps_to_slice_offset_and_out_of_range_is_skipped()
    {
        RenderPlan plan = Compile(NewSliceDocument());
        Assert.That(plan.Voices.Length, Is.EqualTo(3), "三个切片各一声部");
        Assert.That(plan.Voices.Select(v => v.AssetOffsetFrames), Is.EqualTo(new long[] { 0, 800, 1600 }));
    }

    [Test]
    public void Key_without_slice_is_skipped_silently()
    {
        ProjectDocument document = NewSliceDocument();
        document.Assets[0].SlicePoints.Clear(); // 无切片点 → key 全部越界
        RenderPlan plan = Compile(document);
        Assert.That(plan.Voices.Length, Is.EqualTo(0), "无对应切片不出声（优雅跳过）");
    }

    [Test]
    public void Non_slice_channel_keeps_original_offset()
    {
        ProjectDocument document = NewSliceDocument();
        document.Channels[0].SliceMode = false;
        RenderPlan plan = Compile(document);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Voices.Length, Is.EqualTo(3));
            Assert.That(plan.Voices.Select(v => v.AssetOffsetFrames),
                Is.All.EqualTo(0), "非切片通道从头播");
        });
    }
}
