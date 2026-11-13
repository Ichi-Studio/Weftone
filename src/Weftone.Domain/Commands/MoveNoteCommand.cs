namespace Weftone.Domain.Commands;

/// <summary>
/// 移动音符（Piano Roll 拖拽）。拖拽过程中的每一步都以 From=手势起点执行，
/// 经 CommandHistory 事务合并为单条撤销记录。
/// </summary>
public sealed class MoveNoteCommand(int patternId, Note note, long fromTick, int fromKey, long toTick, int toKey) : ICommand
{
    private readonly int _patternId = patternId;
    private readonly long _fromTick = fromTick;
    private readonly int _fromKey = fromKey;
    private readonly long _toTick = toTick;
    private readonly int _toKey = toKey;
    private Note _note = note;

    public string Title => $"移动音符 tick={_fromTick}→{_toTick} key={_fromKey}→{_toKey}";

    public void Apply(ProjectDocument document)
    {
        FindNote(document).Tick = _toTick;
        FindNote(document).Key = _toKey;
    }

    public void Revert(ProjectDocument document)
    {
        FindNote(document).Tick = _fromTick;
        FindNote(document).Key = _fromKey;
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
