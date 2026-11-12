namespace Weftone.Domain;

/// <summary>Playlist 片段：pattern 在时间轴上的摆放（起始 tick，整段展开）。</summary>
public sealed class Clip
{
    public int PatternId { get; set; }
    public long StartTick { get; set; }
}
