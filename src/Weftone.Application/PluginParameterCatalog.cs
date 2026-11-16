using Weftone.Abstractions.Plugins;
using Weftone.Domain;

namespace Weftone.Application;

/// <summary>
/// 插件参数目录（SPEC M3 竖切 3）：按 (path, id) 经临时实例取参数描述并缓存——
/// UI 参数面板用；不触碰播放实例（创建-查询-销毁，UI 线程）。
/// </summary>
public sealed class PluginParameterCatalog
{
    private readonly IPluginInstanceFactory _factory;
    private readonly Dictionary<(string Path, string Id, int SampleRate), IReadOnlyList<PluginParameterInfo>> _cache = [];

    public PluginParameterCatalog(IPluginInstanceFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public IReadOnlyList<PluginParameterInfo> Get(Channel channel, ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(document);
        if (channel.Kind != ChannelKind.Plugin || channel.PluginId is null)
        {
            return [];
        }
        PluginRef reference = document.Plugins.FirstOrDefault(p => p.Id == channel.PluginId)
            ?? throw new InvalidOperationException($"channel {channel.Id} 引用未知插件 {channel.PluginId}");
        var key = (reference.Path, reference.Id, document.SampleRate);
        if (_cache.TryGetValue(key, out IReadOnlyList<PluginParameterInfo>? cached))
        {
            return cached;
        }
        using IAudioPluginInstance probe = _factory.Create(reference.Path, reference.Id, document.SampleRate);
        IReadOnlyList<PluginParameterInfo> parameters = probe.Parameters;
        _cache[key] = parameters;
        return parameters;
    }
}
