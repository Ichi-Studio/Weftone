namespace Weftone.Domain.Commands;

/// <summary>
/// 调整音符长度（Piano Roll 右缘拖拽）。每步以 From=手势起点长度执行，
/// 经 CommandHistory 事务合并为单条撤销记录。
/// </summary>
public sealed class ResizeNoteCommand(int patternId, Note note, long fromLengthTicks, long toLengthTicks) : ICommand
{
    private readonly int _patternId = patternId;
    private readonly long _fromLengthTicks = fromLengthTicks;
    private readonly long _toLengthTicks = toLengthTicks;
    private Note _note = note;

    public string Title => $"调整长度 {_fromLengthTicks}→{_toLengthTicks}";

    public void Apply(ProjectDocument document)
    {
        if (_toLengthTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(toLengthTicks), "长度至少 1 tick");
        }
        FindNote(document).LengthTicks = _toLengthTicks;
    }

    public void Revert(ProjectDocument document)
    {
        FindNote(document).LengthTicks = _fromLengthTicks;
    }

    private Note FindNote(ProjectDocument document)
    {
        Pattern? pattern = document.Patterns.FirstOrDefault(p => p.Id == _patternId)
            ?? throw new InvalidOperationException($"pattern {_patternId} 不存在");
        if (!pattern.Notes.Contains(_note))
        {
            throw new InvalidOperationException("音符不存在");
        }
        return _note;
    }
}
