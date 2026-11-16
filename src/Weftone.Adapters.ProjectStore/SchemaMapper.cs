using System.Text.Json.Serialization;
using Weftone.Domain;

namespace Weftone.Adapters.ProjectStore;

#pragma warning disable CA1812 // DTO 经反序列化实例化

internal sealed class MetadataJson
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("generator")] public string Generator { get; set; } = "";
}

internal sealed class ProjectJson
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("tempo")] public int Tempo { get; set; }
    [JsonPropertyName("sampleRate")] public int SampleRate { get; set; }
    [JsonPropertyName("masterVolume")] public double MasterVolume { get; set; } = 1.0;
    [JsonPropertyName("masterVolumePoints"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AutomationPointJson>? MasterVolumePoints { get; set; }
    [JsonPropertyName("channels")] public List<ChannelJson> Channels { get; set; } = [];
    [JsonPropertyName("patterns")] public List<PatternJson> Patterns { get; set; } = [];
    [JsonPropertyName("playlist")] public List<ClipJson> Playlist { get; set; } = [];
    [JsonPropertyName("assets")] public List<AssetJson> Assets { get; set; } = [];
    [JsonPropertyName("plugins")] public List<PluginRefJson>? Plugins { get; set; }
    [JsonPropertyName("effectPresets")] public Dictionary<string, List<EffectJson>>? EffectPresets { get; set; }
}

internal sealed class ChannelJson
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("volume")] public double Volume { get; set; }
    [JsonPropertyName("pan")] public double Pan { get; set; }
    [JsonPropertyName("assetId")] public string? AssetId { get; set; }
    [JsonPropertyName("pluginId")] public string? PluginId { get; set; }
    [JsonPropertyName("effects"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<EffectJson>? Effects { get; set; }
    [JsonPropertyName("stretchSpeed")] public double? StretchSpeed { get; set; }
    [JsonPropertyName("pluginParameters"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PluginParamJson>? PluginParameters { get; set; }
    [JsonPropertyName("pluginParamPoints"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<AutomationPointJson>>? PluginParamPoints { get; set; }
    [JsonPropertyName("pluginState")] public string? PluginState { get; set; }
    [JsonPropertyName("pluginStateSha256")] public string? PluginStateSha256 { get; set; }
    [JsonPropertyName("volumePoints"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AutomationPointJson>? VolumePoints { get; set; }
    [JsonPropertyName("panPoints"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AutomationPointJson>? PanPoints { get; set; }
}

/// <summary>插件参数静态值（SPEC M3 竖切 3）。</summary>
internal sealed class PluginParamJson
{
    [JsonPropertyName("id")] public uint Id { get; set; }
    [JsonPropertyName("value")] public double Value { get; set; }
}

/// <summary>效果槽（SPEC M5）：kind 字符串 + 参数字典 + 旁路。</summary>
internal sealed class EffectJson
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("bypassed")] public bool Bypassed { get; set; }
    [JsonPropertyName("parameters")] public Dictionary<string, double>? Parameters { get; set; }
    [JsonPropertyName("parameterLanes")] public Dictionary<string, List<AutomationPointJson>>? ParameterLanes { get; set; }
}

/// <summary>插件引用（SPEC M3）：id ↔ channel.pluginId；path 为本地 .clap 路径（插件不入包）。</summary>
internal sealed class PluginRefJson
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

/// <summary>自动化点（SPEC M2，schema v1 可选字段）：lane 内 tick 严格升序、值域随参数。</summary>
internal sealed class AutomationPointJson
{
    [JsonPropertyName("tick")] public long Tick { get; set; }
    [JsonPropertyName("value")] public double Value { get; set; }
}

internal sealed class NoteJson
{
    [JsonPropertyName("channelId")] public int ChannelId { get; set; }
    [JsonPropertyName("tick")] public long Tick { get; set; }
    [JsonPropertyName("key")] public int Key { get; set; }
    [JsonPropertyName("velocity")] public double Velocity { get; set; }
    [JsonPropertyName("lengthTicks")] public long LengthTicks { get; set; }
}

internal sealed class PatternJson
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("notes")] public List<NoteJson> Notes { get; set; } = [];
}

internal sealed class ClipJson
{
    [JsonPropertyName("patternId")] public int PatternId { get; set; }
    [JsonPropertyName("startTick")] public long StartTick { get; set; }
}

internal sealed class AssetJson
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("slicePoints"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<long>? SlicePoints { get; set; }
}

#pragma warning restore CA1812

internal static class SchemaMapper
{
    public static ProjectJson ToJson(ProjectDocument document)
    {
        return new ProjectJson
        {
            SchemaVersion = WeftProjectStore.SchemaVersion,
            Tempo = document.Tempo,
            SampleRate = document.SampleRate,
            MasterVolume = document.MasterVolume,
            MasterVolumePoints = MapLane(document.MasterVolumePoints),
            Channels = document.Channels.Select(c => new ChannelJson
            {
                Id = c.Id, Name = c.Name, Kind = KindToString(c.Kind),
                Volume = c.Volume, Pan = c.Pan, AssetId = c.AssetId, PluginId = c.PluginId,
                PluginState = c.PluginStateEntry,
                PluginStateSha256 = c.PluginStateSha256,
                StretchSpeed = c.StretchSpeed == 1.0 ? null : c.StretchSpeed,
                Effects = c.Effects.Slots.Count == 0
                    ? null
                    : c.Effects.Slots.Select(e => new EffectJson
                    {
                        Kind = e.Kind.ToString(),
                        Bypassed = e.Bypassed,
                        Parameters = e.Parameters.Count == 0 ? null : new Dictionary<string, double>(e.Parameters),
                        ParameterLanes = MapEffectLanes(e),
                    }).ToList(),
                PluginParameters = c.PluginParameters.Count == 0
                    ? null
                    : c.PluginParameters.Select(p => new PluginParamJson { Id = p.Id, Value = p.Value }).ToList(),
                PluginParamPoints = MapPluginParamLanes(c),
                VolumePoints = MapLane(c.VolumePoints),
                PanPoints = MapLane(c.PanPoints),
            }).ToList(),
            Patterns = document.Patterns.Select(p => new PatternJson
            {
                Id = p.Id, Name = p.Name,
                Notes = p.Notes.Select(n => new NoteJson
                {
                    ChannelId = n.ChannelId, Tick = n.Tick, Key = n.Key,
                    Velocity = n.Velocity, LengthTicks = n.LengthTicks,
                }).ToList(),
            }).ToList(),
            Playlist = document.Playlist.Select(c => new ClipJson
            {
                PatternId = c.PatternId, StartTick = c.StartTick,
            }).ToList(),
            Plugins = document.Plugins.Count == 0
                ? null
                : document.Plugins.Select(p => new PluginRefJson { Id = p.Id, Path = p.Path }).ToList(),
            EffectPresets = document.EffectPresets.Count == 0
                ? null
                : document.EffectPresets.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Select(e => new EffectJson
                    {
                        Kind = e.Kind.ToString(),
                        Bypassed = e.Bypassed,
                        Parameters = e.Parameters.Count == 0 ? null : new Dictionary<string, double>(e.Parameters),
                    }).ToList()),
            Assets = document.Assets.Select(a => new AssetJson
            {
                Id = a.Id, Path = a.Path, Sha256 = a.Sha256,
                SlicePoints = a.SlicePoints.Count == 0 ? null : [.. a.SlicePoints],
            }).ToList(),
        };
    }

    public static ProjectDocument ToDomain(ProjectJson json)
    {
        var document = new ProjectDocument
        {
            Tempo = json.Tempo,
            SampleRate = json.SampleRate,
            MasterVolume = json.MasterVolume,
        };
        var channelIds = new HashSet<int>();
        foreach (ChannelJson c in json.Channels)
        {
            Require(channelIds.Add(c.Id), $"channel id {c.Id} 重复");
            document.Channels.Add(new Channel
            {
                Id = c.Id,
                Name = c.Name,
                Kind = ParseKind(c.Kind),
                Volume = c.Volume,
                Pan = c.Pan,
                AssetId = c.AssetId,
                PluginId = c.PluginId,
                PluginStateEntry = c.PluginState,
                PluginStateSha256 = c.PluginStateSha256,
                StretchSpeed = c.StretchSpeed ?? 1.0,
            });
            if (c.Effects is not null)
            {
                foreach (EffectJson e in c.Effects)
                {
                    var slot = new EffectSlot
                    {
                        Kind = ParseEffectKind(e.Kind),
                        Bypassed = e.Bypassed,
                    };
                    if (e.Parameters is not null)
                    {
                        foreach (KeyValuePair<string, double> param in e.Parameters)
                        {
                            Require(double.IsFinite(param.Value), $"效果参数非有限值: {param.Key}={param.Value}");
                            slot.Parameters[param.Key] = param.Value;
                        }
                    }
                    if (e.ParameterLanes is not null)
                    {
                        foreach (KeyValuePair<string, List<AutomationPointJson>> lane in e.ParameterLanes)
                        {
                            slot.GetParameterLane(lane.Key).AddRange(ParseLane(lane.Value,
                                $"effect {e.Kind} parameterLanes[{lane.Key}]",
                                double.MinValue, double.MaxValue));
                        }
                    }
                    document.Channels[^1].Effects.Slots.Add(slot);
                }
            }
            if (c.PluginParameters is not null)
            {
                foreach (PluginParamJson param in c.PluginParameters)
                {
                    document.Channels[^1].PluginParameters.Add(new PluginParameterValue(param.Id, param.Value));
                }
            }
            if (c.PluginParamPoints is not null)
            {
                foreach (KeyValuePair<string, List<AutomationPointJson>> lane in c.PluginParamPoints)
                {
                    if (!uint.TryParse(lane.Key, out uint paramId))
                    {
                        throw new InvalidProjectException($"channel {c.Id} 插件参数 id 非法: {lane.Key}");
                    }
                    List<AutomationPoint> points = document.Channels[^1].GetPluginParamPoints(paramId);
                    points.AddRange(ParseLane(lane.Value,
                        $"channel {c.Id} pluginParamPoints[{paramId}]", double.MinValue, double.MaxValue));
                }
            }
            document.Channels[^1].VolumePoints.AddRange(ParseLane(c.VolumePoints,
                $"channel {c.Id} volumePoints", 0, 2));
            document.Channels[^1].PanPoints.AddRange(ParseLane(c.PanPoints,
                $"channel {c.Id} panPoints", -1, 1));
        }

        if (json.EffectPresets is not null)
        {
            foreach (KeyValuePair<string, List<EffectJson>> preset in json.EffectPresets)
            {
                var slots = new List<EffectSlot>();
                foreach (EffectJson e in preset.Value)
                {
                    var slot = new EffectSlot { Kind = ParseEffectKind(e.Kind), Bypassed = e.Bypassed };
                    if (e.Parameters is not null)
                    {
                        foreach (KeyValuePair<string, double> param in e.Parameters)
                        {
                            slot.Parameters[param.Key] = param.Value;
                        }
                    }
                    slots.Add(slot);
                }
                document.EffectPresets[preset.Key] = slots;
            }
        }

        if (json.Plugins is not null)
        {
            var pluginIds = new HashSet<string>();
            foreach (PluginRefJson p in json.Plugins)
            {
                Require(pluginIds.Add(p.Id), $"plugin id 重复: {p.Id}");
                Require(!string.IsNullOrWhiteSpace(p.Path), $"插件 {p.Id} 缺少 .clap 路径");
                document.Plugins.Add(new PluginRef { Id = p.Id, Path = p.Path });
            }
        }

        document.MasterVolumePoints.AddRange(ParseLane(json.MasterVolumePoints, "masterVolumePoints", 0, 2));

        foreach (AssetJson a in json.Assets)
        {
            Require(!string.IsNullOrWhiteSpace(a.Id), "asset id 为空");
            Require(a.Path.StartsWith("assets/", StringComparison.Ordinal), $"asset 路径越界: {a.Path}");
            Require(IsHex64(a.Sha256), $"asset sha256 非法: {a.Sha256}");
            var assetRef = new AssetRef { Id = a.Id, Path = a.Path, Sha256 = a.Sha256 };
            if (a.SlicePoints is not null)
            {
                long previous = -1;
                foreach (long frame in a.SlicePoints)
                {
                    Require(frame >= 0, $"asset {a.Id} 切片帧越界: {frame}");
                    Require(frame > previous, $"asset {a.Id} 切片帧未严格升序: {frame}");
                    previous = frame;
                    assetRef.SlicePoints.Add(frame);
                }
            }
            document.Assets.Add(assetRef);
        }

        foreach (ChannelJson c in json.Channels)
        {
            if (ParseKind(c.Kind) == ChannelKind.Sampler)
            {
                Require(!string.IsNullOrEmpty(c.AssetId), $"sampler channel {c.Id} 缺少 assetId");
                Require(document.Assets.Any(a => a.Id == c.AssetId),
                    $"channel {c.Id} 引用未知 asset {c.AssetId}");
            }
            if (ParseKind(c.Kind) == ChannelKind.Plugin)
            {
                Require(!string.IsNullOrEmpty(c.PluginId), $"plugin channel {c.Id} 缺少 pluginId");
                Require(document.Plugins.Any(p => p.Id == c.PluginId),
                    $"channel {c.Id} 引用未知 plugin {c.PluginId}");
            }
            if (!string.IsNullOrEmpty(c.PluginState))
            {
                Require(c.PluginState!.StartsWith("presets/", StringComparison.Ordinal),
                    $"channel {c.Id} 插件状态路径越界: {c.PluginState}");
                Require(IsHex64(c.PluginStateSha256 ?? ""), $"channel {c.Id} 插件状态 SHA256 非法");
            }
            if (c.StretchSpeed is { } speed)
            {
                Require(speed >= 0.25 && speed <= 4.0, $"channel {c.Id} stretchSpeed 越界: {speed}");
            }
            Require(c.Volume is >= 0 and <= 2, $"channel {c.Id} 音量越界: {c.Volume}");
            Require(c.Pan is >= -1 and <= 1, $"channel {c.Id} 声像越界: {c.Pan}");
        }

        var patternIds = new HashSet<int>();
        foreach (PatternJson p in json.Patterns)
        {
            Require(patternIds.Add(p.Id), $"pattern id {p.Id} 重复");
            var pattern = new Pattern { Id = p.Id, Name = p.Name };
            foreach (NoteJson n in p.Notes)
            {
                Require(n.Tick >= 0, $"tick 越界: {n.Tick}");
                Require(n.LengthTicks >= 1, $"lengthTicks 越界: {n.LengthTicks}");
                Require(n.Key is >= 0 and <= 127, $"key 越界: {n.Key}");
                Require(n.Velocity is >= 0 and <= 1, $"velocity 越界: {n.Velocity}");
                Require(channelIds.Contains(n.ChannelId), $"note 引用未知 channel {n.ChannelId}");
                pattern.Notes.Add(new Note
                {
                    ChannelId = n.ChannelId, Tick = n.Tick, Key = n.Key,
                    Velocity = n.Velocity, LengthTicks = n.LengthTicks,
                });
            }
            document.Patterns.Add(pattern);
        }

        foreach (ClipJson c in json.Playlist)
        {
            Require(patternIds.Contains(c.PatternId), $"clip 引用未知 pattern {c.PatternId}");
            Require(c.StartTick >= 0, $"clip startTick 越界: {c.StartTick}");
            document.Playlist.Add(new Clip { PatternId = c.PatternId, StartTick = c.StartTick });
        }

        Require(document.Tempo is >= ProjectDocument.MinTempo and <= ProjectDocument.MaxTempo,
            $"BPM 越界: {document.Tempo}");
        Require(document.SampleRate is >= 8000 and <= 192_000,
            $"采样率越界: {document.SampleRate}");
        Require(document.MasterVolume is >= 0 and <= 2,
            $"主控音量越界: {document.MasterVolume}");
        return document;
    }

    private static EffectKind ParseEffectKind(string kind) => kind switch
    {
        nameof(EffectKind.Eq3Band) => EffectKind.Eq3Band,
        nameof(EffectKind.Compressor) => EffectKind.Compressor,
        nameof(EffectKind.Delay) => EffectKind.Delay,
        nameof(EffectKind.Reverb) => EffectKind.Reverb,
        _ => throw new InvalidProjectException($"未知效果器类型: {kind}"),
    };

    private static string KindToString(ChannelKind kind) => kind switch
    {
        ChannelKind.Sine => "sine",
        ChannelKind.Sampler => "sampler",
        ChannelKind.Plugin => "plugin",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>插件参数自动化 lane：paramId 字符串键 → 点（空通道省略，竖切 4）。</summary>
    private static Dictionary<string, List<AutomationPointJson>>? MapPluginParamLanes(Channel channel)
    {
        Dictionary<string, List<AutomationPointJson>>? lanes = null;
        foreach (uint paramId in channel.AutomatedPluginParamIds)
        {
            List<AutomationPoint> points = channel.GetPluginParamPoints(paramId);
            if (points.Count == 0)
            {
                continue;
            }
            lanes ??= [];
            lanes[paramId.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                points.Select(p => new AutomationPointJson { Tick = p.Tick, Value = p.Value }).ToList();
        }
        return lanes;
    }

    /// <summary>空 lane 写 null 省略（旧文件无字段加载为空，双向兼容，SPEC M2）。</summary>
    private static Dictionary<string, List<AutomationPointJson>>? MapEffectLanes(EffectSlot slot)
    {
        Dictionary<string, List<AutomationPointJson>>? lanes = null;
        foreach (string name in slot.AutomatedParameterNames)
        {
            List<AutomationPoint> points = slot.GetParameterLane(name);
            if (points.Count == 0)
            {
                continue;
            }
            lanes ??= [];
            lanes[name] = points.Select(p => new AutomationPointJson { Tick = p.Tick, Value = p.Value }).ToList();
        }
        return lanes;
    }

    private static List<AutomationPointJson>? MapLane(List<AutomationPoint> points)
        => points.Count == 0
            ? null
            : points.Select(p => new AutomationPointJson { Tick = p.Tick, Value = p.Value }).ToList();

    private static List<AutomationPoint> ParseLane(List<AutomationPointJson>? json, string label, double min, double max)
    {
        var points = new List<AutomationPoint>(json?.Count ?? 0);
        if (json is null)
        {
            return points;
        }
        foreach (AutomationPointJson p in json)
        {
            Require(p.Tick >= 0, $"{label} tick 越界: {p.Tick}");
            Require(p.Value >= min && p.Value <= max, $"{label} 值越界: {p.Value}（须在 [{min}, {max}]）");
            Require(points.Count == 0 || points[^1].Tick < p.Tick, $"{label} tick 未严格升序或重复: {p.Tick}");
            points.Add(new AutomationPoint { Tick = p.Tick, Value = p.Value });
        }
        return points;
    }

    private static ChannelKind ParseKind(string kind) => kind switch
    {
        "sine" => ChannelKind.Sine,
        "sampler" => ChannelKind.Sampler,
        "plugin" => ChannelKind.Plugin,
        _ => throw new InvalidProjectException($"未知声部类型: {kind}"),
    };

    private static bool IsHex64(string value)
        => value.Length == 64 && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidProjectException(message);
        }
    }
}
