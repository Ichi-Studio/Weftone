using Weftone.Abstractions.Plugins;
using Weftone.Abstractions.Rendering;

namespace Weftone.Application;

/// <summary>
/// 插件实例宿主（SPEC M3 D5）：按 plan 槽表同步实例——(path, id, 采样率) 相同即复用
/// （保插件内部状态），否则新建；多余实例隔代退役（音频线程换 State 后一代内仍在用，
/// 再下一轮 Sync 才 dispose——in-process 近似，M6 out-of-process 收紧）。
/// </summary>
public sealed class PluginInstanceHost : IDisposable
{
    private readonly IPluginInstanceFactory _factory;
    private readonly Dictionary<(string Path, string Id), IAudioPluginInstance> _instances = [];
    private List<IAudioPluginInstance> _retired = [];

    public PluginInstanceHost(IPluginInstanceFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>产出与 plan.PluginSlots 对齐的实例表（槽序）；编译线程调用。
    /// 开头先释放上一代退役实例（上上次 Sync 之前的表必然已换出）。</summary>
    public IReadOnlyList<IAudioPluginInstance> Sync(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        DisposeRetiredGeneration();
        var table = new IAudioPluginInstance[plan.PluginSlots.Length];
        var keep = new HashSet<(string Path, string Id)>();
        for (int i = 0; i < plan.PluginSlots.Length; i++)
        {
            PlanPluginSlot slot = plan.PluginSlots[i];
            var key = (slot.FilePath, slot.PluginId);
            keep.Add(key);
            if (!_instances.TryGetValue(key, out IAudioPluginInstance? instance)
                || instance.SampleRate != plan.SampleRate)
            {
                if (instance is not null)
                {
                    Retire(instance);
                    _instances.Remove(key);
                }
                instance = _factory.Create(slot.FilePath, slot.PluginId, plan.SampleRate);
                _instances[key] = instance;
            }
            table[i] = instance;
        }
        foreach (KeyValuePair<(string Path, string Id), IAudioPluginInstance> pair in _instances)
        {
            if (!keep.Contains(pair.Key))
            {
                Retire(pair.Value);
            }
        }
        _instances.Keys.Where(k => !keep.Contains(k)).ToList().ForEach(k => _instances.Remove(k));
        return table;
    }

    private void Retire(IAudioPluginInstance instance)
    {
        _retired.Add(instance);
    }

    /// <summary>上一代退役实例释放（调用两次 Sync 之间的实例必然已被换出）。</summary>
    private void DisposeRetiredGeneration()
    {
        foreach (IAudioPluginInstance instance in _retired)
        {
            instance.Dispose();
        }
        _retired = [];
    }

    public void Dispose()
    {
        DisposeRetiredGeneration();
        foreach (IAudioPluginInstance instance in _instances.Values)
        {
            instance.Dispose();
        }
        _instances.Clear();
    }
}
