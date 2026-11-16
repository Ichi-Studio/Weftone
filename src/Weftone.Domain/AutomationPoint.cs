namespace Weftone.Domain;

public enum AutomationParameter
{
    Volume,
    Pan,
    /// <summary>插件参数自动化（SPEC M3 竖切 4）：PluginParamId 标识参数，值域由插件定义（宽松校验）。</summary>
    PluginParam,
    /// <summary>效果参数自动化（SPEC M5 D3）：EffectSlotIndex 标识通道链内槽序，宽松值域。</summary>
    EffectParam,
}

/// <summary>自动化目标：普通通道（vol/pan/插件参数）或主控（master 仅有 volume）。</summary>
public readonly record struct AutomationTarget
{
    public bool IsMaster { get; }
    public int ChannelId { get; }
    /// <summary>AutomationParameter == PluginParam 时为插件参数 id；== EffectParam 时为效果槽序号。</summary>
    public uint PluginParamId { get; }
    /// <summary>AutomationParameter == EffectParam 时的效果参数名。</summary>
    public string? EffectParameterName { get; }

    private AutomationTarget(bool isMaster, int channelId, uint pluginParamId, string? effectParameterName = null)
    {
        IsMaster = isMaster;
        ChannelId = channelId;
        PluginParamId = pluginParamId;
        EffectParameterName = effectParameterName;
    }

    public static AutomationTarget Master { get; } = new(true, 0, 0);

    public static AutomationTarget Channel(int channelId) => new(false, channelId, 0);

    public static AutomationTarget PluginParameter(int channelId, uint paramId) =>
        new(false, channelId, paramId);

    public static AutomationTarget EffectParameter(int channelId, int slotIndex, string parameterName) =>
        new(false, channelId, unchecked((uint)slotIndex), parameterName);

    /// <summary>AutomationParameter == EffectParam 时：效果槽序号。</summary>
    public int EffectSlotIndex => unchecked((int)PluginParamId);

    public override string ToString() => IsMaster ? "master" : $"ch{ChannelId}";
}

/// <summary>自动化关键点（SPEC M2 D2=a）：lane 内 Tick 升序唯一；值域随参数。</summary>
public struct AutomationPoint
{
    public long Tick;
    public double Value;
}

/// <summary>
/// 自动化 lane 存取（SPEC M2）：维护升序唯一不变量，同 tick 冲突=替换（后者覆盖）。
/// 命令与序列化共用，编译期才求值，Domain 不含插值逻辑。
/// </summary>
public static class AutomationLanes
{
    /// <summary>值域：volume/master [0,2]，pan [-1,1]；插件参数由插件定义——
    /// Domain 层无描述，用宽松有限值域（严格 clamp 留编译/UI 层按描述执行）。</summary>
    public static (double Min, double Max) Range(AutomationParameter parameter) => parameter switch
    {
        AutomationParameter.Volume => (0.0, 2.0),
        AutomationParameter.Pan => (-1.0, 1.0),
        AutomationParameter.PluginParam => (-1e9, 1e9),
        AutomationParameter.EffectParam => (-1e9, 1e9),
        _ => throw new ArgumentOutOfRangeException(nameof(parameter)),
    };

    /// <summary>master 仅有 volume；master+pan/插件参数视为建模错误。</summary>
    public static bool Supports(AutomationTarget target, AutomationParameter parameter) =>
        !target.IsMaster || parameter == AutomationParameter.Volume;

    public static List<AutomationPoint> Points(ProjectDocument document, AutomationTarget target, AutomationParameter parameter)
    {
        if (!Supports(target, parameter))
        {
            throw new InvalidOperationException($"master 不支持 {parameter} 自动化");
        }
        if (target.IsMaster)
        {
            return document.MasterVolumePoints;
        }
        Channel channel = document.Channels.FirstOrDefault(c => c.Id == target.ChannelId)
            ?? throw new InvalidOperationException($"channel {target.ChannelId} 不存在");
        if (parameter == AutomationParameter.PluginParam)
        {
            return channel.GetPluginParamPoints(target.PluginParamId);
        }
        if (parameter == AutomationParameter.EffectParam)
        {
            int slotIndex = unchecked((int)target.PluginParamId);
            if (slotIndex < 0 || slotIndex >= channel.Effects.Slots.Count)
            {
                throw new InvalidOperationException($"channel {target.ChannelId} 无效果槽 #{slotIndex}");
            }
            return channel.Effects.Slots[slotIndex].GetParameterLane(target.EffectParameterName ?? "");
        }
        return parameter == AutomationParameter.Volume ? channel.VolumePoints : channel.PanPoints;
    }

    /// <summary>校验值域（命令 Apply 与 schema 加载共用）。</summary>
    public static void ValidateValue(AutomationTarget target, AutomationParameter parameter, double value)
    {
        (double min, double max) = Range(parameter);
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value,
                $"{target} {parameter} 自动化值须在 [{min}, {max}]");
        }
    }

    /// <summary>升序插入；同 tick 已有点则替换并返回旧值。</summary>
    public static AutomationPoint? Insert(List<AutomationPoint> points, AutomationPoint point)
    {
        if (point.Tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(point), point.Tick, "tick 不可为负");
        }
        int index = IndexOfTick(points, point.Tick);
        if (index >= 0)
        {
            AutomationPoint previous = points[index];
            points[index] = point;
            return previous;
        }
        points.Insert(~index, point);
        return null;
    }

    /// <summary>移除 tick 处的点；不存在返回 null。</summary>
    public static AutomationPoint? RemoveAtTick(List<AutomationPoint> points, long tick)
    {
        int index = IndexOfTick(points, tick);
        if (index < 0)
        {
            return null;
        }
        AutomationPoint point = points[index];
        points.RemoveAt(index);
        return point;
    }

    /// <summary>升序 lane 二分查找（返回负值即 ~插入位）。</summary>
    public static int IndexOfTick(List<AutomationPoint> points, long tick)
    {
        int low = 0, high = points.Count - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            long midTick = points[mid].Tick;
            if (midTick == tick)
            {
                return mid;
            }
            if (midTick < tick)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return ~low;
    }
}
