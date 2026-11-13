namespace Weftone.Domain.Commands;

public sealed class AddClipCommand(Clip clip) : ICommand
{
    private readonly Clip _clip = clip;

    public string Title => $"添加 clip pattern={_clip.PatternId} tick={_clip.StartTick}";

    public void Apply(ProjectDocument document)
    {
        if (document.Playlist.Contains(_clip))
        {
            throw new InvalidOperationException("clip 已存在");
        }
        ValidatePattern(document);
        document.Playlist.Add(_clip);
    }

    public void Revert(ProjectDocument document)
    {
        document.Playlist.Remove(_clip);
    }

    private void ValidatePattern(ProjectDocument document)
    {
        if (document.Patterns.All(p => p.Id != _clip.PatternId))
        {
            throw new InvalidOperationException($"pattern {_clip.PatternId} 不存在");
        }
    }
}
