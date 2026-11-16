using Weftone.Abstractions.Rendering;

namespace Weftone.Abstractions.Plugins;

/// <summary>内置效果器节点（SPEC M5）：plan 纯数据——类型 + 参数值 + 每参数增益断点；
/// 引擎侧按节点表实例化 DSP（RCU 换表）。</summary>
public struct RenderEffectNode
{
    /// <summary>所属通道索引（通道内串联）。</summary>
    public int ChannelIndex;
    /// <summary>效果器类型（与 Domain.EffectKind 对齐的稳定 id）。</summary>
    public string Kind;
    /// <summary>参数值（顺序与 Domain 效果参数定义一致）。</summary>
    public double[] Parameters;
    /// <summary>每参数的增益断点（SPEC M5 D3=a：复用 M2 曲线；null = 静态值）。</summary>
    public GainBreakpoint[][]? ParameterCurves;
    /// <summary>旁通（保留参数与自动化位置）。</summary>
    public bool Bypassed;
}
