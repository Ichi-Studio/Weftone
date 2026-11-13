using Weftone.Domain;

namespace Weftone.Domain.Commands;

/// <summary>
/// 删除自动化点（SPEC M2）：按 tick 定位，Apply 捕获被删点，Revert 升序回插。
/// </summary>
public sealed class RemoveAutomationPointCommand(
    AutomationTarget target, AutomationParameter parameter, long tick) : ICommand
{
    private readonly AutomationTarget _target = target;
    private readonly AutomationParameter _parameter = parameter;
    private readonly long _tick = tick;
    private AutomationPoint _removed;
    private bool _applied;

    public string Title => $"{_target} {_parameter} 删点 tick={_tick}";

    public void Apply(ProjectDocument document)
    {
        List<AutomationPoint> points = AutomationLanes.Points(document, _target, _parameter);
        AutomationPoint? removed = AutomationLanes.RemoveAtTick(points, _tick)
            ?? throw new InvalidOperationException($"{_target} {_parameter} tick={_tick} 无点可删");
        _removed = removed.Value;
        _applied = true;
    }

    public void Revert(ProjectDocument document)
    {
        if (!_applied)
        {
            throw new InvalidOperationException("未执行过的命令不可撤销");
        }
        List<AutomationPoint> points = AutomationLanes.Points(document, _target, _parameter);
        points.Insert(~AutomationLanes.IndexOfTick(points, _removed.Tick), _removed);
    }
}
