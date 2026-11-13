using Weftone.Domain;

namespace Weftone.Domain.Commands;

/// <summary>
/// 移动自动化点（SPEC M2，from/to 显式，同 M1b 手势模式）：
/// 拖拽每一步以「点当前所在 tick → 新 tick」执行，手势起点由 VM 持有，
/// 经 CommandHistory 事务合并为单条撤销记录（逆序 Revert 精确还原）。
/// 目标 tick 已有点则替换（后者覆盖），被替换点在 Apply 捕获。
/// </summary>
public sealed class MoveAutomationPointCommand(
    AutomationTarget target, AutomationParameter parameter,
    long currentTick, long newTick, double newValue) : ICommand
{
    private readonly AutomationTarget _target = target;
    private readonly AutomationParameter _parameter = parameter;
    private readonly long _currentTick = currentTick;
    private readonly long _newTick = newTick;
    private readonly double _newValue = newValue;
    private AutomationPoint _moved;
    private AutomationPoint _replaced;
    private bool _hadReplaced;
    private bool _applied;

    public string Title => $"{_target} {_parameter} 移点 tick={_currentTick}→{_newTick} value={_newValue:0.###}";

    public void Apply(ProjectDocument document)
    {
        AutomationLanes.ValidateValue(_target, _parameter, _newValue);
        List<AutomationPoint> points = AutomationLanes.Points(document, _target, _parameter);
        AutomationPoint? moved = AutomationLanes.RemoveAtTick(points, _currentTick);
        if (moved is null)
        {
            throw new InvalidOperationException($"{_target} {_parameter} tick={_currentTick} 无点可移");
        }
        _moved = moved.Value;
        AutomationPoint? replaced = AutomationLanes.Insert(points, new AutomationPoint { Tick = _newTick, Value = _newValue });
        _hadReplaced = replaced.HasValue;
        _replaced = replaced ?? default;
        _applied = true;
    }

    public void Revert(ProjectDocument document)
    {
        if (!_applied)
        {
            throw new InvalidOperationException("未执行过的命令不可撤销");
        }
        List<AutomationPoint> points = AutomationLanes.Points(document, _target, _parameter);
        AutomationLanes.RemoveAtTick(points, _newTick);
        points.Insert(~AutomationLanes.IndexOfTick(points, _moved.Tick), _moved);
        if (_hadReplaced)
        {
            points.Insert(~AutomationLanes.IndexOfTick(points, _replaced.Tick), _replaced);
        }
    }
}
