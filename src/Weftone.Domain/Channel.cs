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
}
