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
    private readonly Dictionary<int, byte[]> _channelStates = [];
    private List<IAudioPluginInstance> _retired = [];

    public PluginInstanceHost(IPluginInstanceFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>产出与 plan.PluginSlots 对齐的实例表（槽序）并应用静态参数（竖切 3）；
    /// 编译线程调用。开头先释放上一代退役实例（上上次 Sync 之前的表必然已换出）。</summary>
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
                // 工程保存的 state chunk：创建后恢复；失败优雅降级（丢 state 保实例可用）
                if (_channelStates.TryGetValue(slot.ChannelId, out byte[]? state))
                {
                    _ = instance.LoadState(state);
                }
            }
            foreach (PlanPluginParam param in slot.Params)
            {
                instance.SetParameter(param.Id, param.Value);
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

    /// <summary>注入工程读入的 state（竖切 3）：缓存并在后续新建实例时恢复。</summary>
    public void ApplyChannelStates(IReadOnlyDictionary<int, byte[]> states)
    {
        foreach (KeyValuePair<int, byte[]> pair in states)
        {
            _channelStates[pair.Key] = pair.Value;
        }
    }

    /// <summary>抓取当前实例的 state chunk（保存工程用）：channelId → bytes；无状态插件跳过。</summary>
    public IReadOnlyDictionary<int, byte[]> CaptureChannelStates(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var states = new Dictionary<int, byte[]>();
        foreach (PlanPluginSlot slot in plan.PluginSlots)
        {
            if (_instances.TryGetValue((slot.FilePath, slot.PluginId), out IAudioPluginInstance? instance))
            {
                byte[]? state = instance.SaveState();
                if (state is not null)
                {
                    states[slot.ChannelId] = state;
                }
            }
        }
        return states;
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
