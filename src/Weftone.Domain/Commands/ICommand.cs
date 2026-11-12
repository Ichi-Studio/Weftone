namespace Weftone.Domain.Commands;

/// <summary>可撤销编辑命令（命令日志 + 事务，ADR-0003）。Apply/Revert 必须精确互逆。</summary>
public interface ICommand
{
    string Title { get; }

    void Apply(ProjectDocument document);

    void Revert(ProjectDocument document);
}
