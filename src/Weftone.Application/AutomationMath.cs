using Weftone.Domain;

namespace Weftone.Application;

/// <summary>
/// 自动化 lane 的 tick 空间求值（SPEC M2 D2=a，仅编译期/UI 使用，RT 不可见）：
/// FL 风格 clamp——首点前/末点后取端点值；两点间线性插值；空 lane 取静态回退值。
/// </summary>
public static class AutomationMath
{
    public static double Evaluate(List<AutomationPoint> points, double fallback, long tick)
    {
        if (points.Count == 0)
        {
            return fallback;
        }
        if (tick <= points[0].Tick)
        {
            return points[0].Value;
        }
        if (tick >= points[^1].Tick)
        {
            return points[^1].Value;
        }
        int index = AutomationLanes.IndexOfTick(points, tick);
        if (index >= 0)
        {
            return points[index].Value;
        }
        AutomationPoint before = points[~index - 1];
        AutomationPoint after = points[~index];
        double t = (double)(tick - before.Tick) / (after.Tick - before.Tick);
        return before.Value + (after.Value - before.Value) * t;
    }
}
