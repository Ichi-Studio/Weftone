using NUnit.Framework;
using Weftone.Abstractions.Rendering;
using Weftone.Application;
using Weftone.Domain;

namespace Weftone.Application.Tests;

/// <summary>SPEC M2 竖切 1：断点编译确定性、首末点 clamp、vol/pan 并集、无自动化退化现状。</summary>
public class AutomationCompileTests
{
    private const int SampleRate = 48_000;
    private const int TicksPerBeat = TimeBase.Ppq; // 96

    private ProjectDocument _document = null!;

    [SetUp]
    public void SetUp()
    {
        _document = new ProjectDocument { Tempo = 120, SampleRate = SampleRate };
        _document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Sine, Volume = 1.0 });
        var pattern = new Pattern { Id = 10 };
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Velocity = 0.8, LengthTicks = 384 });
        _document.Patterns.Add(pattern);
        _document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
    }

    /// <summary>120 BPM、PPQ 96：1 tick = 48_000*60/(120*96) = 250 样本，无截断误差。</summary>
    private const long SamplesPerTick = 250;

    private static void AddPoint(List<AutomationPoint> lane, long tick, double value)
        => AutomationLanes.Insert(lane, new AutomationPoint { Tick = tick, Value = value });

    [Test]
    public void Channel_without_automation_compiles_to_static_plan()
    {
        RenderPlan plan = new DocumentCompiler().Compile(_document);
        Assert.Multiple(() =>
        {
            Assert.That(plan.ChannelGainCurves, Is.Empty, "无自动化不建曲线");
            Assert.That(plan.MasterGainCurve, Is.Null);
            Assert.That(plan.Voices[0].GainCurveIndex, Is.EqualTo(-1), "静态声部走预乘增益");
            Assert.That(plan.Voices[0].GainLeft, Is.EqualTo(0.8f).Within(1e-6), "velocity × volume × pan 现状路径");
        });
    }

    [Test]
    public void Volume_curve_has_clamp_breakpoints_at_both_ends()
    {
        AddPoint(_document.Channels[0].VolumePoints, TicksPerBeat, 0.25); // tick 96
        AddPoint(_document.Channels[0].VolumePoints, TicksPerBeat * 2, 1.5); // tick 192
        RenderPlan plan = new DocumentCompiler().Compile(_document);

        Assert.That(plan.ChannelGainCurves.Length, Is.EqualTo(1));
        GainBreakpoint[] curve = plan.ChannelGainCurves[0];
        Assert.Multiple(() =>
        {
            Assert.That(curve.Length, Is.EqualTo(4), "首 clamp + 2 点 + 末 clamp");
            Assert.That(curve[0].Sample, Is.EqualTo(0));
            Assert.That(curve[0].GainLeft, Is.EqualTo(0.25f).Within(1e-6), "首点前 clamp 到首点值");
            Assert.That(curve[1].Sample, Is.EqualTo(TicksPerBeat * SamplesPerTick));
            Assert.That(curve[1].GainLeft, Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(curve[2].Sample, Is.EqualTo(TicksPerBeat * 2 * SamplesPerTick));
            Assert.That(curve[2].GainLeft, Is.EqualTo(1.5f).Within(1e-6));
            Assert.That(curve[^1].Sample, Is.EqualTo(plan.TotalSamples));
            Assert.That(curve[^1].GainLeft, Is.EqualTo(1.5f).Within(1e-6), "末点后 clamp 到末点值");
            Assert.That(curve[0].GainRight, Is.EqualTo(curve[0].GainLeft).Within(1e-7), "pan 静态 0 双侧同值");
        });
    }

    [Test]
    public void Point_beyond_timeline_end_extends_interpolation_to_end_breakpoint()
    {
        // 时间轴 = 384 tick（2 小节 @120BPM）；末点在 288，其后插值延伸到末断点
        AddPoint(_document.Channels[0].VolumePoints, TicksPerBeat * 3, 2.0); // tick 288 < 384
        AddPoint(_document.Channels[0].VolumePoints, TicksPerBeat * 5, 0.0); // tick 480 > 384，轴外
        RenderPlan plan = new DocumentCompiler().Compile(_document);

        GainBreakpoint[] curve = plan.ChannelGainCurves[0];
        Assert.That(curve[^1].Sample, Is.EqualTo(plan.TotalSamples));
        // 288→480 线性插值到 384 处应为一半：vol=1.0
        Assert.That(curve[^1].GainLeft, Is.EqualTo(1.0f).Within(1e-6), "末断点值=按 tick 插值到时间轴末端");
        Assert.That(curve.Length, Is.EqualTo(3), "轴外点不单独成断点（首 clamp + 288 + 末断点）");
    }

    [Test]
    public void Volume_and_pan_lanes_merge_into_union_breakpoints()
    {
        AddPoint(_document.Channels[0].VolumePoints, 96, 0.5);
        AddPoint(_document.Channels[0].VolumePoints, 288, 1.0);
        AddPoint(_document.Channels[0].PanPoints, 192, -1.0);
        RenderPlan plan = new DocumentCompiler().Compile(_document);

        GainBreakpoint[] curve = plan.ChannelGainCurves[0];
        Assert.That(curve.Length, Is.EqualTo(5), "首 clamp + tick 并集 3 + 末 clamp");
        Assert.Multiple(() =>
        {
            // tick 192：vol 插值 (0.5+1.0)/2=0.75，pan=-1 → 只剩左
            Assert.That(curve[2].Sample, Is.EqualTo(192 * SamplesPerTick));
            Assert.That(curve[2].GainLeft, Is.EqualTo(0.75f).Within(1e-6));
            Assert.That(curve[2].GainRight, Is.EqualTo(0f).Within(1e-7));
        });
    }

    [Test]
    public void Empty_volume_lane_with_pan_points_uses_static_volume_fallback()
    {
        _document.Channels[0].Volume = 2.0;
        AddPoint(_document.Channels[0].PanPoints, 0, 0.5);
        RenderPlan plan = new DocumentCompiler().Compile(_document);
        GainBreakpoint[] curve = plan.ChannelGainCurves[0];
        Assert.Multiple(() =>
        {
            // PanLaw(+0.5) = (0.5, 1.0)：左侧衰减、右侧保持
            Assert.That(curve[0].GainLeft, Is.EqualTo(1.0f).Within(1e-6), "vol 回退静态值 2.0 × pan 左 0.5");
            Assert.That(curve[0].GainRight, Is.EqualTo(2.0f).Within(1e-6));
        });
    }

    [Test]
    public void Voice_of_automated_channel_carries_velocity_and_curve_index()
    {
        AddPoint(_document.Channels[0].VolumePoints, 0, 1.0);
        RenderPlan plan = new DocumentCompiler().Compile(_document);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Voices[0].GainCurveIndex, Is.EqualTo(0));
            Assert.That(plan.Voices[0].GainLeft, Is.EqualTo(0.8f).Within(1e-7), "曲线声部只持 velocity");
            Assert.That(plan.Voices[0].GainRight, Is.EqualTo(0.8f).Within(1e-7));
        });
    }

    [Test]
    public void Master_volume_curve_compiles_and_null_when_empty()
    {
        RenderPlan plain = new DocumentCompiler().Compile(_document);
        Assert.That(plain.MasterGainCurve, Is.Null);

        AddPoint(_document.MasterVolumePoints, 192, 0.5);
        AddPoint(_document.MasterVolumePoints, 384, 1.0);
        RenderPlan plan = new DocumentCompiler().Compile(_document);
        GainBreakpoint[] curve = plan.MasterGainCurve!;
        Assert.Multiple(() =>
        {
            Assert.That(curve.Length, Is.EqualTo(3), "首 clamp + 192 + 末断点（384 点与末断点同位合并）");
            Assert.That(curve[0].GainLeft, Is.EqualTo(0.5f).Within(1e-6), "首点前 clamp");
            Assert.That(curve[^1].Sample, Is.EqualTo(plan.TotalSamples));
            Assert.That(curve[^1].GainLeft, Is.EqualTo(1.0f).Within(1e-6));
            Assert.That(curve[^1].GainRight, Is.EqualTo(curve[^1].GainLeft).Within(1e-7), "master L==R");
        });
    }

    [Test]
    public void Pattern_mode_curve_spans_pattern_length()
    {
        AddPoint(_document.Channels[0].VolumePoints, 192, 0.5);
        RenderPlan plan = new DocumentCompiler().CompilePattern(_document, 10, []);
        Assert.Multiple(() =>
        {
            Assert.That(plan.TotalSamples, Is.EqualTo(4 * TicksPerBeat * SamplesPerTick), "1 小节向上取整");
            Assert.That(plan.ChannelGainCurves, Has.Length.EqualTo(1));
            Assert.That(plan.ChannelGainCurves[0][^1].Sample, Is.EqualTo(plan.TotalSamples), "末 clamp 在循环长度处");
        });
    }

    [Test]
    public void Multiple_channels_map_to_distinct_curve_indices()
    {
        _document.Channels.Add(new Channel { Id = 2, Kind = ChannelKind.Sine });
        _document.Patterns[0].Notes.Add(new Note { ChannelId = 2, Tick = 0, Velocity = 1.0, LengthTicks = 96 });
        AddPoint(_document.Channels[1].VolumePoints, 0, 0.5);
        AddPoint(_document.Channels[0].PanPoints, 0, 0.25);

        RenderPlan plan = new DocumentCompiler().Compile(_document);
        Assert.That(plan.ChannelGainCurves.Length, Is.EqualTo(2));
        RenderVoice ch1Voice = plan.Voices.Single(v => Math.Abs(v.GainLeft - 0.8f) < 1e-3f);
        RenderVoice ch2Voice = plan.Voices.Single(v => Math.Abs(v.GainLeft - 1.0f) < 1e-3f);
        Assert.Multiple(() =>
        {
            Assert.That(ch1Voice.GainCurveIndex, Is.EqualTo(0), "ch1 pan 曲线");
            Assert.That(ch2Voice.GainCurveIndex, Is.EqualTo(1), "ch2 vol 曲线");
        });
    }

    [Test]
    public void Same_curve_compiles_bitwise_identical_twice()
    {
        AddPoint(_document.Channels[0].VolumePoints, 96, 0.5);
        AddPoint(_document.Channels[0].PanPoints, 140, -0.75);
        AddPoint(_document.MasterVolumePoints, 192, 1.25);
        var compiler = new DocumentCompiler();

        RenderPlan first = compiler.Compile(_document);
        RenderPlan second = compiler.Compile(_document);

        Assert.Multiple(() =>
        {
            Assert.That(second.ChannelGainCurves.Length, Is.EqualTo(first.ChannelGainCurves.Length));
            for (int c = 0; c < first.ChannelGainCurves.Length; c++)
            {
                Assert.That(second.ChannelGainCurves[c].Length, Is.EqualTo(first.ChannelGainCurves[c].Length));
                for (int b = 0; b < first.ChannelGainCurves[c].Length; b++)
                {
                    Assert.That(second.ChannelGainCurves[c][b].Sample, Is.EqualTo(first.ChannelGainCurves[c][b].Sample), $"曲线{c}断点{b}");
                    Assert.That(second.ChannelGainCurves[c][b].GainLeft, Is.EqualTo(first.ChannelGainCurves[c][b].GainLeft), $"曲线{c}断点{b} L");
                    Assert.That(second.ChannelGainCurves[c][b].GainRight, Is.EqualTo(first.ChannelGainCurves[c][b].GainRight), $"曲线{c}断点{b} R");
                }
            }
            Assert.That(second.MasterGainCurve!.Select(b => (b.Sample, b.GainLeft, b.GainRight)),
                Is.EqualTo(first.MasterGainCurve!.Select(b => (b.Sample, b.GainLeft, b.GainRight))));
        });
    }

    [Test]
    public void Automation_math_evaluates_with_fl_style_clamps()
    {
        var lane = new List<AutomationPoint>();
        AddPoint(lane, 96, 0.5);
        AddPoint(lane, 192, 1.5);
        Assert.Multiple(() =>
        {
            Assert.That(AutomationMath.Evaluate(lane, 9.9, 0), Is.EqualTo(0.5), "首点前 clamp");
            Assert.That(AutomationMath.Evaluate(lane, 9.9, 300), Is.EqualTo(1.5), "末点后 clamp");
            Assert.That(AutomationMath.Evaluate(lane, 9.9, 96), Is.EqualTo(0.5));
            Assert.That(AutomationMath.Evaluate(lane, 9.9, 144), Is.EqualTo(1.0).Within(1e-12), "中点插值");
            Assert.That(AutomationMath.Evaluate([], 0.75, 42), Is.EqualTo(0.75), "空 lane 回退静态值");
        });
    }
}
