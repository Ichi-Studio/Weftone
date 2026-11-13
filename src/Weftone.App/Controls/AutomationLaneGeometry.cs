using Weftone.Domain;

namespace Weftone.App.Controls;

/// <summary>
/// 自动化 lane 几何/命中纯函数（SPEC M2：不依赖 Avalonia 类型，headless 单测覆盖）。
/// 横轴与 Piano Roll 同轴（tick↔pixel 线性）；纵轴参数值域 [min,max] 顶=最大，
/// 上下各留 Padding 余量。折线段在首点前/末点后按 FL 式 clamp 平直延伸。
/// </summary>
public static class AutomationLaneGeometry
{
    public const double LaneHeight = 96;
    public const double Padding = 10;
    public const double PointRadius = 4;
    public const double HitRadius = 9;

    public static double ValueToY(double value, double min, double max)
    {
        double t = max > min ? (Math.Clamp(value, min, max) - min) / (max - min) : 0.5;
        return LaneHeight - Padding - t * (LaneHeight - 2 * Padding);
    }

    public static double YToValue(double y, double min, double max)
    {
        double t = (LaneHeight - Padding - y) / (LaneHeight - 2 * Padding);
        return min + Math.Clamp(t, 0, 1) * (max - min);
    }

    /// <summary>命中测试：任一点距 (x, y) 在 HitRadius 像素内（与绘制同一 min/max 映射）。</summary>
    public static AutomationPoint? HitTest(IReadOnlyList<AutomationPoint> points, double x, double y,
        double min, double max)
    {
        AutomationPoint? best = null;
        double bestDistance = double.MaxValue;
        foreach (AutomationPoint point in points)
        {
            double dx = PianoRollGeometry.TickToX(point.Tick) - x;
            double dy = ValueToY(point.Value, min, max) - y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }
        return bestDistance <= HitRadius ? best : null;
    }

    /// <summary>视口裁剪：可见 tick 范围外两侧各含一个邻点，保证折线延伸到视口边缘。</summary>
    public static List<AutomationPoint> VisibleWithNeighbors(
        IReadOnlyList<AutomationPoint> points, long firstTick, long lastTick)
    {
        var visible = new List<AutomationPoint>();
        int before = -1;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].Tick < firstTick)
            {
                before = i;
                continue;
            }
            if (points[i].Tick > lastTick)
            {
                visible.Add(points[i]);
                break;
            }
            visible.Add(points[i]);
        }
        if (before >= 0)
        {
            visible.Insert(0, points[before]);
        }
        return visible;
    }
}
