namespace Weftone.Domain;

public enum ChannelKind
{
    Sine,
    Sampler,
}

/// <summary>声部通道：音源类型 + 混音参数（volume 0..2，pan -1..1 线性声像）。</summary>
public sealed class Channel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ChannelKind Kind { get; set; }
    public double Volume { get; set; } = 1.0;
    public double Pan { get; set; }
    public string? AssetId { get; set; }
    /// <summary>volume 自动化点（SPEC M2）：非空时时间轴上按曲线覆盖 Volume，Tick 升序唯一。</summary>
    public List<AutomationPoint> VolumePoints { get; } = [];
    /// <summary>pan 自动化点（SPEC M2）：非空时覆盖 Pan。</summary>
    public List<AutomationPoint> PanPoints { get; } = [];
}
