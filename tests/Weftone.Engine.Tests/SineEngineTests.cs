using NUnit.Framework;
using Weftone.Engine;

namespace Weftone.Engine.Tests;

public class SineEngineTests
{
    private const double TwoPi = 6.283185307179586;

    [Test]
    public void Sample_matches_direct_formula()
    {
        var engine = new SineEngine(440f, 48_000);
        float[] buffer = new float[512];
        engine.Render(buffer);
        for (int n = 0; n < buffer.Length; n++)
        {
            float expected = MathF.Sin((float)(TwoPi * 440.0 * n / 48_000.0));
            Assert.That(buffer[n], Is.EqualTo(expected).Within(1e-4f), $"样本 {n}");
        }
    }

    [Test]
    public void Render_is_deterministic()
    {
        var a = new SineEngine(440f, 48_000);
        var b = new SineEngine(440f, 48_000);
        float[] left = new float[100_000];
        float[] right = new float[100_000];
        a.Render(left);
        b.Render(right);
        Assert.That(left, Is.EqualTo(right));
    }

    [Test]
    public void Render_does_not_allocate()
    {
        RtAssert.Reset();
        var engine = new SineEngine(440f, 48_000);
        float[] buffer = new float[512];
        RtAssert.Guard(engine.Callback, buffer);
        for (int i = 0; i < 1_000; i++)
        {
            RtAssert.Guard(engine.Callback, buffer);
        }
        Assert.That(RtAssert.Violations, Is.EqualTo(0),
            "渲染路径出现托管分配（RT_ASSERT 构建下此断言有效；Release 为空过）");
    }
}
