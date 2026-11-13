namespace Weftone.Domain;

public enum AutomationParameter
{
    Volume,
    Pan,
}

/// <summary>自动化目标：普通通道或主控（master 仅有 volume，SPEC M2 D3=a）。</summary>
public readonly record struct AutomationTarget
{
    public bool IsMaster { get; }
    public int ChannelId { get; }

    private AutomationTarget(bool isMaster, int channelId)
    {
        IsMaster = isMaster;
        ChannelId = channelId;
    }

    public static AutomationTarget Master { get; } = new(true, 0);

    public static AutomationTarget Channel(int channelId) => new(false, channelId);

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
    /// <summary>值域：volume/master [0,2]，pan [-1,1]（与混音参数一致）。</summary>
    public static (double Min, double Max) Range(AutomationParameter parameter) => parameter switch
    {
        AutomationParameter.Volume => (0.0, 2.0),
        AutomationParameter.Pan => (-1.0, 1.0),
        _ => throw new ArgumentOutOfRangeException(nameof(parameter)),
    };

    /// <summary>master 仅有 volume；master+pan 视为建模错误。</summary>
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
