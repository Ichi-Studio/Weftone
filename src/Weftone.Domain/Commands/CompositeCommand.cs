namespace Weftone.Domain.Commands;

/// <summary>
/// 命令组合（事务）：Apply 按序执行，Revert 逆序回滚，作为单条撤销记录入栈。
/// 用于拖拽手势（按下→移动→释放）合并为一次撤销。
/// </summary>
public sealed class CompositeCommand(string title, List<ICommand> commands) : ICommand
{
    public IReadOnlyList<ICommand> Commands { get; } = commands;

    public string Title { get; } = title;

    public void Apply(ProjectDocument document)
    {
        foreach (ICommand command in Commands)
        {
            command.Apply(document);
        }
    }

    public void Revert(ProjectDocument document)
    {
        for (int i = Commands.Count - 1; i >= 0; i--)
        {
            Commands[i].Revert(document);
        }
    }
}
