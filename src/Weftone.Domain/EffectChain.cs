using Weftone.Domain;

namespace Weftone.Domain;

/// <summary>内置效果器类型（SPEC M5 D1=a：EQ / 压缩 / 延迟 / FDN 混响）。</summary>
public enum EffectKind
{
    /// <summary>三段均衡（低架 / 中峰 / 高架），参数：低频增益 dB、中频增益 dB、高频增益 dB。</summary>
    Eq3Band = 0,
    /// <summary>压缩器，参数：阈值 dB、比率、attack ms、release ms。</summary>
    Compressor = 1,
    /// <summary>延迟，参数：时间 ms、反馈 0..1、混响量 0..1。</summary>
    Delay = 2,
    /// <summary>FDN 混响，参数：衰减时间 s、尺寸、预延迟 ms。</summary>
    Reverb = 3,
}

/// <summary>效果器槽（SPEC M5 D2=a：通道内串联顺序）。</summary>
public sealed class EffectSlot
{
    public EffectKind Kind { get; set; }
    /// <summary>参数值（键为效果器内参数名；默认参数由 DSP 侧提供）。</summary>
    public Dictionary<string, double> Parameters { get; } = [];
    /// <summary>旁通（保留参数与自动化位置，仅不参与处理）。</summary>
    public bool Bypassed { get; set; }

    private readonly Dictionary<string, List<AutomationPoint>> _parameterLanes = [];

    /// <summary>效果参数自动化 lane（SPEC M5 D3）：参数名 → 点列表（升序唯一）。</summary>
    public List<AutomationPoint> GetParameterLane(string parameterName)
    {
        if (!_parameterLanes.TryGetValue(parameterName, out List<AutomationPoint>? lane))
        {
            lane = [];
            _parameterLanes[parameterName] = lane;
        }
        return lane;
    }

    /// <summary>带自动化点的参数名集合。</summary>
    public IReadOnlyCollection<string> AutomatedParameterNames => _parameterLanes.Keys;
}

/// <summary>通道效果链（SPEC M5）：顺序即数组序；引擎侧按顺序串联。</summary>
public sealed class EffectChain
{
    public List<EffectSlot> Slots { get; } = [];
}
