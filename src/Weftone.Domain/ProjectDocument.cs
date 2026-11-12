namespace Weftone.Domain;

/// <summary>
/// 可编辑工程文档（ADR-0003：富对象、可撤销、可序列化；音频线程永不可见）。
/// M1a 单线程同步编辑。
/// </summary>
public sealed class ProjectDocument
{
    public const int MinTempo = 20;
    public const int MaxTempo = 999;

    public int Tempo { get; set; } = 140;
    public int SampleRate { get; set; } = 48_000;
    public List<Channel> Channels { get; } = [];
    public List<Pattern> Patterns { get; } = [];
    public List<Clip> Playlist { get; } = [];
    public List<AssetRef> Assets { get; } = [];
}
