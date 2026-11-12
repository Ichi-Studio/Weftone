using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Engine;

namespace Weftone.Engine.Tests;

public class PlanRendererTests
{
    private const int SampleRate = 48_000;

    private static RenderPlan NewPlan(long totalSamples, params RenderVoice[] voices) => new()
    {
        SampleRate = SampleRate,
        Channels = 2,
        TotalSamples = totalSamples,
        Voices = voices,
    };

    private static RenderVoice SineVoice(long start, long length, float freq = 440f,
        float gainLeft = 0.5f, float gainRight = 0.5f) => new()
    {
        StartSample = start,
        LengthSamples = length,
        Kind = RenderVoiceKind.Sine,
        Frequency = freq,
        GainLeft = gainLeft,
        GainRight = gainRight,
        AssetIndex = -1,
    };

    [Test]
    public void Voice_starts_silently_then_renders_sine()
    {
        var renderer = new PlanRenderer();
        renderer.Load(NewPlan(600, SineVoice(100, 400)));
        float[] buffer = new float[2 * 256];
        renderer.Render(buffer);
        Assert.That(buffer[2 * 50], Is.EqualTo(0f));
        Assert.That(buffer[2 * 100], Is.EqualTo(0f), "起始样本之前的帧");
        float expected = MathF.Sin(2f * MathF.PI * 440f * 50 / SampleRate) * 0.5f;
        Assert.That(buffer[2 * 150], Is.EqualTo(expected).Within(1e-4f));
        Assert.That(buffer[2 * 150 + 1], Is.EqualTo(expected).Within(1e-4f));
    }

    [Test]
    public void Voice_fades_out_before_gate_end_without_click()
    {
        var renderer = new PlanRenderer();
        renderer.Load(NewPlan(600, SineVoice(0, 256)));
        float[] buffer = new float[2 * 600];
        renderer.Render(buffer);
        double peakTail = 0;
        for (int i = 254; i < 256; i++)
        {
            peakTail = Math.Max(peakTail, Math.Abs(buffer[2 * i]));
        }
        Assert.That(peakTail, Is.LessThan(0.02f), "结尾包络应趋近 0");
        Assert.That(buffer[2 * 300], Is.EqualTo(0f), "门后静音");
    }

    [Test]
    public void Polyphony_sums_voices()
    {
        var renderer = new PlanRenderer();
        renderer.Load(NewPlan(300,
            SineVoice(0, 300, 440f, 1f, 1f),
            SineVoice(0, 300, 660f, 1f, 1f)));
        float[] buffer = new float[2 * 300];
        renderer.Render(buffer);
        int n = 150;
        float expected = MathF.Sin(2f * MathF.PI * 440f * n / SampleRate)
                       + MathF.Sin(2f * MathF.PI * 660f * n / SampleRate);
        Assert.That(buffer[2 * n], Is.EqualTo(expected).Within(1e-3f));
    }

    [Test]
    public void Overfull_pool_steals_and_never_crashes()
    {
        const int voices = 70;
        var events = new RenderVoice[voices];
        for (int i = 0; i < voices; i++)
        {
            events[i] = SineVoice(0, 4_800, 220f + i, 1f, 1f);
        }
        var renderer = new PlanRenderer();
        renderer.Load(NewPlan(4_800, events));
        float[] buffer = new float[2 * 256];
        for (int block = 0; block < 19; block++)
        {
            renderer.Render(buffer);
        }
        Assert.That(renderer.DroppedEvents, Is.EqualTo(0), "pending 环 32 应消化 6 个溢出事件");
    }

    [Test]
    public void Render_does_not_allocate()
    {
        var renderer = new PlanRenderer();
        var events = new RenderVoice[8];
        for (int i = 0; i < events.Length; i++)
        {
            events[i] = SineVoice(i * 64, 4_800);
        }
        renderer.Load(NewPlan(4_800, events));
        float[] buffer = new float[2 * 256];
        AudioRenderCallback callback = renderer.Render;
        RtAssert.Guard(callback, buffer);
        for (int i = 0; i < 1_000; i++)
        {
            RtAssert.Guard(callback, buffer);
        }
        Assert.That(RtAssert.Violations, Is.EqualTo(0),
            "渲染路径出现托管分配（RT_ASSERT 构建下此断言有效）");
    }

    private static RenderVoice SamplerVoice(long start, long gate, float rate = 1f,
        float gainLeft = 1f, float gainRight = 1f, int assetIndex = 0) => new()
    {
        StartSample = start,
        LengthSamples = gate,
        Kind = RenderVoiceKind.Sampler,
        Frequency = rate,
        GainLeft = gainLeft,
        GainRight = gainRight,
        AssetIndex = assetIndex,
    };

    private static PlanAsset MonoAsset(params float[] samples) => new()
    {
        Samples = samples,
        Channels = 1,
        Frames = samples.Length,
    };

    [Test]
    public void Sampler_plays_mono_asset_verbatim_at_unit_rate()
    {
        var renderer = new PlanRenderer();
        renderer.Load(new RenderPlan
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = 6,
            Voices = [SamplerVoice(0, 512)],
            Assets = [MonoAsset(0.25f, -0.5f, 0.75f, 1f)],
        });
        float[] buffer = new float[2 * 4];
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(buffer[0], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(buffer[1], Is.EqualTo(0.25f).Within(1e-6f), "mono 双声道同值");
            Assert.That(buffer[2], Is.EqualTo(-0.5f).Within(1e-6f));
            Assert.That(buffer[3], Is.EqualTo(-0.5f).Within(1e-6f));
        });
    }

    [Test]
    public void Sampler_interpolates_at_half_rate()
    {
        var renderer = new PlanRenderer();
        renderer.Load(new RenderPlan
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = 8,
            Voices = [SamplerVoice(0, 512, rate: 0.5f)],
            Assets = [MonoAsset(0f, 1f, 0f)],
        });
        float[] buffer = new float[2 * 4];
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(buffer[0], Is.EqualTo(0f).Within(1e-6f), "pos 0");
            Assert.That(buffer[2], Is.EqualTo(0.5f).Within(1e-6f), "pos 0.5 线性插值");
            Assert.That(buffer[4], Is.EqualTo(1f).Within(1e-6f), "pos 1");
            Assert.That(buffer[6], Is.EqualTo(0.5f).Within(1e-6f), "pos 1.5 线性插值");
        });
    }

    [Test]
    public void Sampler_stereo_asset_maps_channels_with_gains()
    {
        var renderer = new PlanRenderer();
        var stereo = new PlanAsset
        {
            Samples = [0.5f, -0.5f, 0.25f, -0.25f],
            Channels = 2,
            Frames = 2,
        };
        renderer.Load(new RenderPlan
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = 4,
            Voices = [SamplerVoice(0, 512, gainLeft: 1f, gainRight: 0.5f)],
            Assets = [stereo],
        });
        float[] buffer = new float[2 * 2];
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(buffer[0], Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(buffer[1], Is.EqualTo(-0.25f).Within(1e-6f));
        });
    }

    [Test]
    public void Sampler_falls_silent_when_asset_exhausts_before_gate()
    {
        var renderer = new PlanRenderer();
        renderer.Load(new RenderPlan
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = 8,
            Voices = [SamplerVoice(0, 512)],
            Assets = [MonoAsset(0.5f, 0.5f, 0.5f)],
        });
        float[] buffer = new float[2 * 6];
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(buffer[0], Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(buffer[4], Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(buffer[6], Is.EqualTo(0f).Within(1e-6f), "资产耗尽后静音");
        });
    }
}
