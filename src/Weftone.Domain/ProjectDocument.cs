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
    /// <summary>主控音量 [0,2]，1.0 = 不衰减（M1b Mixer master 推子）。</summary>
    public double MasterVolume { get; set; } = 1.0;
    /// <summary>master volume 自动化点（SPEC M2 D3=a）：master 仅有 volume lane。</summary>
    public List<AutomationPoint> MasterVolumePoints { get; } = [];
    public List<Channel> Channels { get; } = [];
    public List<Pattern> Patterns { get; } = [];
    public List<Clip> Playlist { get; } = [];
    public List<AssetRef> Assets { get; } = [];
    /// <summary>工程引用的 CLAP 插件（SPEC M3）：Id ↔ Channel.PluginId；Path 为本地 .clap 路径。</summary>
    public List<PluginRef> Plugins { get; } = [];
}
