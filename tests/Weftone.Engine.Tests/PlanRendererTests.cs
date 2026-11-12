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
}
