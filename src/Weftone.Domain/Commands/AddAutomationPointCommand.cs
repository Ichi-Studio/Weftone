using Weftone.Domain;

namespace Weftone.Domain.Commands;

/// <summary>
/// 添加自动化点（SPEC M2）：同 tick 冲突=替换，被替换点在首次 Apply 捕获、Revert 精确还原。
/// 拖拽画点经 CommandHistory 事务合并为单条撤销记录。
/// </summary>
public sealed class AddAutomationPointCommand(
    AutomationTarget target, AutomationParameter parameter, AutomationPoint point) : ICommand
{
    private readonly AutomationTarget _target = target;
    private readonly AutomationParameter _parameter = parameter;
    private readonly AutomationPoint _point = point;
    private bool _applied;
    private AutomationPoint _replaced;
    private bool _hadReplaced;

    public string Title => $"{_target} {_parameter} +点 tick={_point.Tick} value={_point.Value:0.###}";

    public void Apply(ProjectDocument document)
    {
        AutomationLanes.ValidateValue(_target, _parameter, _point.Value);
        List<AutomationPoint> points = AutomationLanes.Points(document, _target, _parameter);
        AutomationPoint? previous = AutomationLanes.Insert(points, _point);
        _hadReplaced = previous.HasValue;
        _replaced = previous ?? default;
        _applied = true;
    }

    public void Revert(ProjectDocument document)
    {
        if (!_applied)
        {
            throw new InvalidOperationException("未执行过的命令不可撤销");
        }
        List<AutomationPoint> points = AutomationLanes.Points(document, _target, _parameter);
        AutomationLanes.RemoveAtTick(points, _point.Tick);
        if (_hadReplaced)
        {
            points.Insert(~AutomationLanes.IndexOfTick(points, _replaced.Tick), _replaced);
        }
    }
}
