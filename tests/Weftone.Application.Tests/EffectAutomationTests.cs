using NUnit.Framework;
using Weftone.Abstractions.Rendering;
using Weftone.Application;
using Weftone.Domain;

namespace Weftone.Application.Tests;

/// <summary>SPEC M5 竖切 3：效果参数 lane → plan 曲线（首末 clamp/并集/定序）。</summary>
public class EffectAutomationTests
{
    private static ProjectDocument DocumentWithEqLane()
    {
        var document = new ProjectDocument { Tempo = 120, SampleRate = 48_000 };
        document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Sine });
        var eq = new EffectSlot { Kind = EffectKind.Eq3Band };
        eq.Parameters["low"] = 0;
        document.Channels[0].Effects.Slots.Add(eq);
        List<AutomationPoint> lane = eq.GetParameterLane("low");
        AutomationLanes.Insert(lane, new AutomationPoint { Tick = 0, Value = -6 });
        AutomationLanes.Insert(lane, new AutomationPoint { Tick = 192, Value = 6 });
        var pattern = new Pattern { Id = 10 };
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Velocity = 1, LengthTicks = 384 });
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        return document;
    }

    [Test]
    public void Effect_param_lane_compiles_to_curve_with_clamps()
    {
        RenderPlan plan = new DocumentCompiler().Compile(DocumentWithEqLane());
        Assert.That(plan.EffectNodes.Length, Is.EqualTo(1));
        Assert.That(plan.EffectNodes[0].ParameterCurves, Is.Not.Null, "带 lane 的参数位得曲线");
        GainBreakpoint[] curve = plan.EffectNodes[0].ParameterCurves![0]!;
        Assert.Multiple(() =>
        {
            Assert.That(curve[0].Sample, Is.EqualTo(0));
            Assert.That(curve[0].GainLeft, Is.EqualTo(-6f).Within(1e-6), "首点");
            Assert.That(curve[1].Sample, Is.EqualTo(192 * 250));
            Assert.That(curve[1].GainLeft, Is.EqualTo(6f).Within(1e-6));
            Assert.That(curve[^1].Sample, Is.EqualTo(plan.TotalSamples), "末 clamp");
            Assert.That(curve[^1].GainLeft, Is.EqualTo(6f).Within(1e-6), "末点后 clamp 末值");
        });
    }

    [Test]
    public void Lane_without_points_or_unknown_name_is_ignored()
    {
        ProjectDocument document = DocumentWithEqLane();
        document.Channels[0].Effects.Slots[0].GetParameterLane("low").Clear();
        RenderPlan plan = new DocumentCompiler().Compile(document);
        Assert.That(plan.EffectNodes[0].ParameterCurves, Is.Null, "空 lane 不产曲线");
    }
}
