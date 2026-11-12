namespace Weftone.Domain;

/// <summary>音符：tick 为 PPQ=96 整数拍位；key 0..127；velocity 0..1。</summary>
public sealed class Note
{
    public int ChannelId { get; set; }
    public long Tick { get; set; }
    public int Key { get; set; } = 60;
    public double Velocity { get; set; } = 1.0;
    public long LengthTicks { get; set; } = TimeBase.Ppq;
}
