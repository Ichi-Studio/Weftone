using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Engine;

namespace Weftone.Engine.Tests;

/// <summary>SPEC M2 竖切 1：块内增益斜坡正确性（解析对比）、跨块连续、循环回绕、master 曲线、零分配。
/// 用常数 mono 采样资产直接观测每样本增益（voice 输出 = velocity × 曲线插值）。</summary>
public class AutomationRenderTests
{
    private const int SampleRate = 48_000;

    private static GainBreakpoint Bp(long sample, float left, float right) => new()
    {
        Sample = sample, GainLeft = left, GainRight = right,
    };

    private static RenderVoice ConstantVoice(int curveIndex, long length = 4_800, float velocity = 1f) => new()
    {
        StartSample = 0,
        LengthSamples = length,
        Kind = RenderVoiceKind.Sampler,
        Frequency = 1f,
        GainLeft = velocity,
        GainRight = velocity,
        AssetIndex = 0,
        GainCurveIndex = curveIndex,
    };

    private static RenderPlan ConstantPlan(GainBreakpoint[] curve, long totalSamples,
        RenderVoice? voice = null, GainBreakpoint[]? master = null)
    {
        // 常数 mono 资产须长于观测窗口：资产耗尽会提前终止声部（M1b 语义）
        var ones = new float[20_000];
        Array.Fill(ones, 1f);
        return new RenderPlan
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = totalSamples,
            Voices = [voice ?? ConstantVoice(0)],
            Assets = [new PlanAsset { Samples = ones, Channels = 1, Frames = ones.Length }],
            ChannelGainCurves = [curve],
            MasterGainCurve = master,
        };
    }

    [Test]
    public void Linear_ramp_matches_analytic_gain_per_sample()
    {
        const long end = 4_800;
        // 声部长于观测窗口，避开门尾 FadeTail 包络
        RenderPlan plan = ConstantPlan([Bp(0, 0f, 0.5f), Bp(end, 1f, 0.5f)], end,
            voice: ConstantVoice(0, length: 2 * end));
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        float[] buffer = new float[2 * end];
        renderer.Render(buffer);

        for (int i = 0; i < end; i += 97)
        {
            float t = i / (float)end;
            float expectedLeft = 1f * (0f + (1f - 0f) * t);
            float expectedRight = 1f * (0.5f + (0.5f - 0.5f) * t);
            Assert.That(buffer[2 * i], Is.EqualTo(expectedLeft).Within(1e-6f), $"左增益 @样本{i}");
            Assert.That(buffer[2 * i + 1], Is.EqualTo(expectedRight).Within(1e-6f), $"右增益 @样本{i}");
        }
    }

    [Test]
    public void Gain_clamps_to_endpoint_values_outside_breakpoint_span()
    {
        RenderPlan plan = ConstantPlan([Bp(100, 0.5f, 0.5f), Bp(200, 1f, 1f)], 4_800);
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        float[] buffer = new float[2 * 600];
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(buffer[2 * 50], Is.EqualTo(0.5f).Within(1e-6f), "首断点前取首值");
            Assert.That(buffer[2 * 150], Is.EqualTo(0.75f).Within(1e-6f), "两断点间中点");
            Assert.That(buffer[2 * 500], Is.EqualTo(1f).Within(1e-6f), "末断点后取末值");
        });
    }

    /// <summary>SPEC 风险项：声部跨块边界（断点间跨块）须连续插值——任意分块与整块渲染逐位一致。</summary>
    [Test]
    public void Block_split_rendering_is_bitwise_identical_to_single_render()
    {
        const long total = 4_800;
        RenderPlan plan = ConstantPlan(
            [Bp(0, 0.1f, 0.9f), Bp(1_000, 1f, 0.2f), Bp(2_500, 0.3f, 0.7f), Bp(4_000, 1.2f, 0.4f)], total);

        var whole = new PlanRenderer();
        whole.Load(plan);
        float[] wholeBuffer = new float[2 * total];
        whole.Render(wholeBuffer);

        var split = new PlanRenderer();
        split.Load(plan);
        float[] splitBuffer = new float[2 * total];
        int[] sizes = [1, 7, 251, 999, 1_024, 1_024, 1_000, 137, 497];
        int offset = 0, s = 0;
        while (offset < total)
        {
            int frames = Math.Min(sizes[s % sizes.Length], (int)total - offset);
            Span<float> slice = splitBuffer.AsSpan(offset * 2, frames * 2);
            split.Render(slice);
            offset += frames;
            s++;
        }
        Assert.That(splitBuffer, Is.EqualTo(wholeBuffer), "跨块/跨断点插值必须逐位连续");
    }

    [Test]
    public void Pan_swing_interpolates_opposite_directions()
    {
        const long end = 4_800;
        RenderPlan plan = ConstantPlan([Bp(0, 1f, 0f), Bp(end, 0f, 1f)], end);
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        float[] buffer = new float[2 * end];
        renderer.Render(buffer);
        int mid = (int)(end / 2);
        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(buffer[2 * mid] - buffer[2 * mid + 1]), Is.LessThan(1e-6f), "中点左右相等");
            Assert.That(buffer[2 * 100], Is.EqualTo(1f - 100f / end).Within(1e-6f), "左声道线性衰减");
            Assert.That(buffer[2 * 100 + 1], Is.EqualTo(100f / (float)end).Within(1e-6f), "右声道线性上升");
        });
    }

    /// <summary>Pattern 循环回绕：自动化跟随回绕后的位置，第二圈与第一圈逐位一致。</summary>
    [Test]
    public void Loop_wrap_resyncs_gain_cursor_to_repeated_curve()
    {
        const int loopEnd = 2_400;
        var ones = new float[20_000];
        Array.Fill(ones, 1f);
        RenderPlan plan = new RenderPlan
        {
            SampleRate = SampleRate,
            Channels = 2,
            TotalSamples = loopEnd,
            LoopStartSample = 0,
            LoopEndSample = loopEnd,
            Voices = [ConstantVoice(0, length: loopEnd)],
            Assets = [new PlanAsset { Samples = ones, Channels = 1, Frames = ones.Length }],
            ChannelGainCurves = [[Bp(0, 0.25f, 0.25f), Bp(loopEnd, 1f, 1f)]],
        };
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        float[] firstLoop = new float[2 * loopEnd];
        float[] secondLoop = new float[2 * loopEnd];
        renderer.Render(firstLoop);
        renderer.Render(secondLoop);
        Assert.That(secondLoop, Is.EqualTo(firstLoop), "回绕后曲线从头跟随（游标二分重定位）");
    }

    [Test]
    public void Master_curve_applies_per_sample_ramp()
    {
        const long end = 2_400;
        RenderPlan plan = ConstantPlan(
            [Bp(0, 1f, 1f), Bp(end, 1f, 1f)], end,
            voice: ConstantVoice(-1), // 静态声部增益 1
            master: [Bp(0, 0f, 0f), Bp(end, 2f, 2f)]);
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        float[] buffer = new float[2 * end];
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(buffer[0], Is.EqualTo(0f).Within(1e-6f));
            Assert.That(buffer[2 * 1_200], Is.EqualTo(1f).Within(1e-6f), "中点 = 曲线一半");
            Assert.That(buffer[2 * 600], Is.EqualTo(0.5f).Within(1e-6f), "1/4 点 = 曲线 1/4");
        });
    }

    [Test]
    public void Fade_tail_envelope_multiplies_with_curve_gain()
    {
        // 长度 128 的声部：门尾 FadeTail 与曲线斜坡相乘，无曲线增益时也应有淡出（现状）
        RenderPlan plan = ConstantPlan([Bp(0, 1f, 1f), Bp(4_800, 1f, 1f)], 4_800,
            voice: ConstantVoice(0, length: 128));
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        float[] buffer = new float[2 * 256];
        renderer.Render(buffer);
        Assert.Multiple(() =>
        {
            Assert.That(buffer[0], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(buffer[2 * 127], Is.EqualTo(1f / 128f).Within(1e-6f), "末样本 = 1/128 淡出包络");
            Assert.That(buffer[2 * 200], Is.EqualTo(0f), "门后静音");
        });
    }

    [Test]
    public void Automation_render_does_not_allocate()
    {
        const long total = 9_600;
        RenderPlan plan = ConstantPlan(
            [Bp(0, 0.1f, 0.9f), Bp(3_000, 1f, 0.2f), Bp(total, 0.4f, 0.8f)], total,
            voice: ConstantVoice(0, length: total),
            master: [Bp(0, 1f, 1f), Bp(total, 0.5f, 0.5f)]);
        var renderer = new PlanRenderer();
        renderer.Load(plan);
        float[] buffer = new float[2 * 256];
        AudioRenderCallback callback = renderer.Render;
        for (long done = 0; done < total; done += 256)
        {
            RtAssert.Guard(callback, buffer);
        }
        for (int i = 0; i < 1_000; i++)
        {
            RtAssert.Guard(callback, buffer);
        }
        Assert.That(RtAssert.Violations, Is.EqualTo(0),
            "自动化渲染路径出现托管分配（RT_ASSERT 构建下此断言有效）");
    }
}
