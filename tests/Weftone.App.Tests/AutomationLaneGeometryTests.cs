using NUnit.Framework;
using Weftone.App.Controls;
using Weftone.Domain;

namespace Weftone.App.Tests;

/// <summary>SPEC M2 竖切 2：lane 几何纯函数——值↔像素映射、命中、视口裁剪邻点。</summary>
public class AutomationLaneGeometryTests
{
    private const double LaneHeight = AutomationLaneGeometry.LaneHeight;
    private const double Padding = AutomationLaneGeometry.Padding;

    [Test]
    public void Value_y_mapping_is_symmetric_and_clamped()
    {
        double minY = AutomationLaneGeometry.ValueToY(2.0, 0, 2);
        double maxY = AutomationLaneGeometry.ValueToY(0.0, 0, 2);
        double midY = AutomationLaneGeometry.ValueToY(1.0, 0, 2);
        Assert.Multiple(() =>
        {
            Assert.That(minY, Is.EqualTo(Padding).Within(1e-9), "最大值在顶部 padding 处");
            Assert.That(maxY, Is.EqualTo(LaneHeight - Padding).Within(1e-9), "最小值在底部");
            Assert.That(midY, Is.EqualTo(LaneHeight / 2).Within(1e-9));
            Assert.That(AutomationLaneGeometry.ValueToY(3.0, 0, 2), Is.EqualTo(minY), "越上界 clamp");
            Assert.That(AutomationLaneGeometry.ValueToY(-1.0, 0, 2), Is.EqualTo(maxY), "越下界 clamp");
        });
        Assert.That(AutomationLaneGeometry.YToValue(minY, 0, 2), Is.EqualTo(2.0).Within(1e-9));
        Assert.That(AutomationLaneGeometry.YToValue(maxY, 0, 2), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(AutomationLaneGeometry.YToValue(-50, 0, 2), Is.EqualTo(2.0), "像素越顶 clamp 到最大值");
        Assert.That(AutomationLaneGeometry.YToValue(9999, 0, 2), Is.EqualTo(0.0), "像素越底 clamp 到最小值");
    }

    [Test]
    public void Pan_axis_spans_minus_one_to_one()
    {
        Assert.That(AutomationLaneGeometry.ValueToY(1.0, -1, 1), Is.EqualTo(Padding).Within(1e-9));
        Assert.That(AutomationLaneGeometry.ValueToY(-1.0, -1, 1), Is.EqualTo(LaneHeight - Padding).Within(1e-9));
        Assert.That(AutomationLaneGeometry.YToValue(LaneHeight / 2, -1, 1), Is.EqualTo(0.0).Within(1e-9));
    }

    private static double X(long tick) => PianoRollGeometry.TickToX(tick);

    [Test]
    public void Hit_test_finds_point_within_radius_and_rejects_far()
    {
        var points = new List<AutomationPoint>
        {
            new() { Tick = 96, Value = 1.0 },
            new() { Tick = 192, Value = 0.25 },
        };
        double y96 = AutomationLaneGeometry.ValueToY(1.0, 0, 2);
        AutomationPoint? hit = AutomationLaneGeometry.HitTest(points, X(96) + 3, y96 + 3, 0, 2);
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.Value.Tick, Is.EqualTo(96));

        Assert.That(AutomationLaneGeometry.HitTest(points, X(96) + 50, y96, 0, 2), Is.Null, "横向太远");
        Assert.That(AutomationLaneGeometry.HitTest(points, X(96), y96 + 40, 0, 2), Is.Null, "纵向太远");
        Assert.That(AutomationLaneGeometry.HitTest(points, X(500), y96, 0, 2), Is.Null, "空 lane 区域");
    }

    [Test]
    public void Visible_with_neighbors_includes_one_point_beyond_each_edge()
    {
        var points = new List<AutomationPoint>();
        foreach (long tick in (long[])[0, 96, 192, 288, 384])
        {
            points.Add(new AutomationPoint { Tick = tick, Value = 0.5 });
        }
        List<AutomationPoint> visible = AutomationLaneGeometry.VisibleWithNeighbors(points, firstTick: 96, lastTick: 192);
        Assert.That(visible.Select(p => p.Tick), Is.EqualTo(new long[] { 0, 96, 192, 288 }),
            "视口内全部 + 两侧各一个邻点（折线延伸到边缘）");

        List<AutomationPoint> all = AutomationLaneGeometry.VisibleWithNeighbors(points, 0, 384);
        Assert.That(all.Select(p => p.Tick), Is.EqualTo(new long[] { 0, 96, 192, 288, 384 }), "全可见时不多取");

        List<AutomationPoint> before = AutomationLaneGeometry.VisibleWithNeighbors(points, 288, 384);
        Assert.That(before.Select(p => p.Tick), Is.EqualTo(new long[] { 192, 288, 384 }));
    }
}
