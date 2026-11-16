using NUnit.Framework;
using Weftone.Abstractions.Rendering;
using Weftone.Engine;

namespace Weftone.Engine.Tests;

/// <summary>SPEC M4 竖切 2：Sampler 声部从资产内偏移起播（切片）。</summary>
public class SliceOffsetRenderTests
{
    [Test]
    public void Sampler_starts_at_asset_offset()
    {
        var renderer = new PlanRenderer();
        var plan = new RenderPlan
        {
            SampleRate = 48_000,
            Channels = 2,
            TotalSamples = 256,
            Voices =
            [
                new RenderVoice
                {
                    StartSample = 0,
                    LengthSamples = 256,
                    Kind = RenderVoiceKind.Sampler,
                    Frequency = 1f,
                    GainLeft = 1f,
                    GainRight = 1f,
                    AssetIndex = 0,
                    AssetOffsetFrames = 2,
                },
            ],
            Assets =
            [
                new PlanAsset { Samples = [0.1f, 0.2f, 0.3f, 0.4f], Channels = 1, Frames = 4 },
            ],
        };
        var buffer = new float[2 * 256];
        renderer.Load(plan);
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(buffer[0], Is.EqualTo(0.3f).Within(1e-6f), "从偏移帧起播");
            Assert.That(buffer[2], Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(buffer[4], Is.EqualTo(0f).Within(1e-6f), "资产耗尽后静音");
        });
    }

    [Test]
    public void Zero_offset_keeps_original_behaviour()
    {
        var renderer = new PlanRenderer();
        var plan = new RenderPlan
        {
            SampleRate = 48_000,
            Channels = 2,
            TotalSamples = 256,
            Voices =
            [
                new RenderVoice
                {
                    StartSample = 0, LengthSamples = 256, Kind = RenderVoiceKind.Sampler,
                    Frequency = 1f, GainLeft = 1f, GainRight = 1f, AssetIndex = 0,
                },
            ],
            Assets = [new PlanAsset { Samples = [0.1f, 0.2f, 0.3f, 0.4f], Channels = 1, Frames = 4 }],
        };
        var buffer = new float[2 * 256];
        renderer.Load(plan);
        renderer.Render(buffer);
        Assert.That(buffer[0], Is.EqualTo(0.1f).Within(1e-6f), "从头播（行为不变）");
    }
}
