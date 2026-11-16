namespace Weftone.App.Controls;

/// <summary>
/// 波形/切片几何纯函数（SPEC M4 竖切 3，无 Avalonia 依赖，headless 单测）：
/// 帧 ↔ 像素线性映射，切片点命中与最近点查找。
/// </summary>
public static class WaveformGeometry
{
    public const double PanelHeight = 96;

    /// <summary>帧 → x（0..width），按总帧数线性映射。</summary>
    public static double FrameToX(long frame, long totalFrames, double width)
        => totalFrames <= 0 ? 0 : Math.Clamp((double)frame / totalFrames, 0, 1) * width;

    /// <summary>x → 帧（取整，clamp 到 [0, totalFrames]）。</summary>
    public static long XToFrame(double x, long totalFrames, double width)
    {
        if (totalFrames <= 0 || width <= 0)
        {
            return 0;
        }
        double ratio = Math.Clamp(x / width, 0, 1);
        return Math.Clamp((long)Math.Round(ratio * totalFrames), 0, totalFrames);
    }

    /// <summary>命中切片点（像素半径内最近者）；无则 null。</summary>
    public static long? HitTestSlice(IReadOnlyList<long> slicePoints, double x, long totalFrames, double width,
        double radiusPixels = 6)
    {
        long? best = null;
        double bestDistance = double.MaxValue;
        foreach (long frame in slicePoints)
        {
            double distance = Math.Abs(FrameToX(frame, totalFrames, width) - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = frame;
            }
        }
        return bestDistance <= radiusPixels ? best : null;
    }
}
