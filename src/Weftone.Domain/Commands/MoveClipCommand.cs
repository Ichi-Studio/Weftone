namespace Weftone.Domain.Commands;

/// <summary>
/// 移动 clip（Playlist 拖拽）：可同时改起始 tick 与所在轨（PatternId）。
/// 拖拽每步以 From=手势起点执行，经 CommandHistory 事务合并为单条撤销记录。
/// </summary>
public sealed class MoveClipCommand(Clip clip, long fromTick, int fromPatternId, long toTick, int toPatternId) : ICommand
{
    private readonly Clip _clip = clip;
    private readonly long _fromTick = fromTick;
    private readonly int _fromPatternId = fromPatternId;
    private readonly long _toTick = toTick;
    private readonly int _toPatternId = toPatternId;

    public string Title => $"移动 clip {_fromTick}→{_toTick} pattern={_fromPatternId}→{_toPatternId}";

    public void Apply(ProjectDocument document)
    {
        if (_toTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toTick), "起始 tick 不得为负");
        }
        if (document.Patterns.All(p => p.Id != _toPatternId))
        {
            throw new InvalidOperationException($"pattern {_toPatternId} 不存在");
        }
        FindClip(document).StartTick = _toTick;
        FindClip(document).PatternId = _toPatternId;
    }

    public void Revert(ProjectDocument document)
    {
        Clip clip = FindClip(document);
        clip.StartTick = _fromTick;
        clip.PatternId = _fromPatternId;
    }

    private Clip FindClip(ProjectDocument document)
    {
        if (!document.Playlist.Contains(_clip))
        {
            throw new InvalidOperationException("clip 不存在");
        }
        return _clip;
    }
}
