namespace Weftone.Application;

/// <summary>
/// 线性声像（M1a 拍板 D4）：pan=0 双侧全量；pan=-1 只剩左；pan=+1 只剩右；
/// 每侧从中心向对侧线性衰减。升级 constant-power 走 ADR。
/// </summary>
public static class PanLaw
{
    public static (float Left, float Right) Linear(double pan)
    {
        double p = Math.Clamp(pan, -1.0, 1.0);
        return ((float)(p <= 0 ? 1.0 : 1.0 - p), (float)(p >= 0 ? 1.0 : 1.0 + p));
    }
}
