using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Engine;

namespace Weftone.Engine.Tests;

public class PlanRendererLoopTests
{
    private const int SampleRate = 48_000;

    private static RenderPlan LoopPlan(long loopEnd, params RenderVoice[] voices) => new()
    {
        SampleRate = SampleRate,
        Channels = 2,
        TotalSamples = loopEnd,
        LoopStartSample = 0,
        LoopEndSample = loopEnd,
        Voices = voices,
    };

    private static RenderVoice SineVoice(long start, long length) => new()
    {
        StartSample = start,
        LengthSamples = length,
        Kind = RenderVoiceKind.Sine,
        Frequency = 440f,
        GainLeft = 0.5f,
        GainRight = 0.5f,
        AssetIndex = -1,
    };

    [Test]
    public void Loop_wraps_position_and_retriggers_events()
    {
        var renderer = new PlanRenderer();
        renderer.Load(LoopPlan(4_800, SineVoice(0, 2_400)));
        float[] buffer = new float[2 * 2_400];

        renderer.Render(buffer);
        Assert.That(renderer.Position, Is.EqualTo(2_400));
        Array.Clear(buffer);
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(renderer.Position, Is.EqualTo(4_800), "到 loopEnd");
            Assert.That(buffer[2 * 100], Is.EqualTo(0f), "音符已结束，回绕前静音");
        });

        Array.Clear(buffer);
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(renderer.Position, Is.EqualTo(2_400), "回绕后继续推进");
            Assert.That(Math.Abs(buffer[2 * 100]), Is.GreaterThan(0.1f), "事件游标归零，音符重新触发");
        });
    }

    [Test]
    public void Loop_shorter_than_block_wraps_multiple_times()
    {
        var renderer = new PlanRenderer();
        renderer.Load(LoopPlan(100, SineVoice(0, 80)));
        float[] buffer = new float[2 * 256];
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(renderer.Position, Is.EqualTo(56), "256 = 100 + 100 + 56");
            Assert.That(Math.Abs(buffer[2 * 25]), Is.GreaterThan(0.1f), "第一段正弦");
            Assert.That(Math.Abs(buffer[2 * 125]), Is.GreaterThan(0.1f), "第二次回绕后仍有信号");
            Assert.That(renderer.DroppedEvents, Is.EqualTo(0));
        });
    }

    [Test]
    public void Loop_render_does_not_allocate()
    {
        var renderer = new PlanRenderer();
        renderer.Load(LoopPlan(300, SineVoice(0, 100), SineVoice(150, 100)));
        float[] buffer = new float[2 * 256];
        AudioRenderCallback callback = renderer.Render;
        RtAssert.Guard(callback, buffer);
        for (int i = 0; i < 1_000; i++)
        {
            RtAssert.Guard(callback, buffer);
        }
        Assert.That(RtAssert.Violations, Is.EqualTo(0),
            "循环回绕路径出现托管分配（RT_ASSERT 构建下此断言有效）");
    }
}
