namespace Weftone.Domain;

public enum ChannelKind
{
    Sine,
    Sampler,
    /// <summary>CLAP 插件乐器通道（SPEC M3 D3=a）：PluginId 指向 Document.Plugins。</summary>
    Plugin,
}

/// <summary>插件引用（SPEC M3）：工程记录 id + 本地 .clap 路径（插件不入包，按用户安装解析）。</summary>
public sealed class PluginRef
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>通道级插件参数值（SPEC M3 竖切 3）：静态 set；自动化曲线留竖切 4。</summary>
public sealed record PluginParameterValue(uint Id, double Value)
{
    public double Value { get; set; } = Value;
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
    /// <summary>Kind == Plugin 时指向 Document.Plugins 的插件 id。</summary>
    public string? PluginId { get; set; }
    /// <summary>插件参数静态值（SPEC M3 竖切 3）：编译进槽表，实例创建时 set。</summary>
    public List<PluginParameterValue> PluginParameters { get; } = [];
    /// <summary>插件 state chunk 的包内路径（presets/*.bin，SPEC M3 竖切 3）；空 = 无保存状态。</summary>
    public string? PluginStateEntry { get; set; }
    /// <summary>state chunk SHA256（加载校验，同资产）。</summary>
    public string? PluginStateSha256 { get; set; }
    /// <summary>volume 自动化点（SPEC M2）：非空时时间轴上按曲线覆盖 Volume，Tick 升序唯一。</summary>
    public List<AutomationPoint> VolumePoints { get; } = [];
    /// <summary>pan 自动化点（SPEC M2）：非空时覆盖 Pan。</summary>
    public List<AutomationPoint> PanPoints { get; } = [];
}
