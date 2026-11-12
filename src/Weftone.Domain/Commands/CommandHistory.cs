namespace Weftone.Domain.Commands;

/// <summary>命令日志：执行即 Apply 入栈；Undo/Redo 空栈拒绝；Redo 在新命令后清空。</summary>
public sealed class CommandHistory
{
    private readonly Stack<ICommand> _undo = new();
    private readonly Stack<ICommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Execute(ICommand command, ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Apply(document);
        _undo.Push(command);
        _redo.Clear();
    }

    public void Undo(ProjectDocument document)
    {
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("撤销栈为空");
        }
        ICommand command = _undo.Pop();
        command.Revert(document);
        _redo.Push(command);
    }

    public void Redo(ProjectDocument document)
    {
        if (_redo.Count == 0)
        {
            throw new InvalidOperationException("重做栈为空");
        }
        ICommand command = _redo.Pop();
        command.Apply(document);
        _undo.Push(command);
    }
}
