namespace Weftone.Domain.Commands;

public sealed class RemoveClipCommand(Clip clip) : ICommand
{
    private readonly Clip _clip = clip;

    public string Title => $"删除 clip pattern={_clip.PatternId} tick={_clip.StartTick}";

    public void Apply(ProjectDocument document)
    {
        if (!document.Playlist.Remove(_clip))
        {
            throw new InvalidOperationException("clip 不存在");
        }
    }

    public void Revert(ProjectDocument document)
    {
        document.Playlist.Add(_clip);
    }
}
