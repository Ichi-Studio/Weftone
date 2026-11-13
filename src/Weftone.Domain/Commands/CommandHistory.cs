namespace Weftone.Domain.Commands;

/// <summary>
/// 命令日志：执行即 Apply 入栈；Undo/Redo 空栈拒绝；Redo 在新命令后清空。
/// 事务（Begin/EndTransaction）把一次手势的多条命令合并为单条撤销记录（M1b 拖拽合并）。
/// </summary>
public sealed class CommandHistory
{
    private readonly Stack<ICommand> _undo = new();
    private readonly Stack<ICommand> _redo = new();
    private List<ICommand>? _transactionCommands;
    private string _transactionTitle = "";

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool InTransaction => _transactionCommands is not null;

    public void Execute(ICommand command, ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Apply(document);
        if (_transactionCommands is null)
        {
            _undo.Push(command);
        }
        else
        {
            _transactionCommands.Add(command);
        }
        _redo.Clear();
    }

    public void BeginTransaction(string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        if (_transactionCommands is not null)
        {
            throw new InvalidOperationException("事务已开启，不支持嵌套");
        }
        _transactionTitle = title;
        _transactionCommands = [];
    }

    public void EndTransaction()
    {
        if (_transactionCommands is null)
        {
            throw new InvalidOperationException("未开启事务");
        }
        List<ICommand> commands = _transactionCommands;
        _transactionCommands = null;
        if (commands.Count == 0)
        {
            return;
        }
        _undo.Push(new CompositeCommand(_transactionTitle, commands));
    }

    public void Undo(ProjectDocument document)
    {
        if (InTransaction)
        {
            throw new InvalidOperationException("事务进行中不可撤销");
        }
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
        if (InTransaction)
        {
            throw new InvalidOperationException("事务进行中不可重做");
        }
        if (_redo.Count == 0)
        {
            throw new InvalidOperationException("重做栈为空");
        }
        ICommand command = _redo.Pop();
        command.Apply(document);
        _undo.Push(command);
    }

    /// <summary>载入新工程时清空撤销/重做栈。</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _transactionCommands = null;
    }
}
