namespace Weftone.Domain.Commands;

public sealed class RemoveNoteCommand(int patternId, Note note) : ICommand
{
    private readonly int _patternId = patternId;
    private Note _note = note;

    public string Title => $"删除音符 tick={_note.Tick} key={_note.Key}";

    public void Apply(ProjectDocument document)
    {
        if (!FindPattern(document).Notes.Remove(_note))
        {
            throw new InvalidOperationException("音符不存在");
        }
    }

    public void Revert(ProjectDocument document)
    {
        FindPattern(document).Notes.Add(_note);
    }

    private Pattern FindPattern(ProjectDocument document)
    {
        Pattern? pattern = document.Patterns.FirstOrDefault(p => p.Id == _patternId);
        return pattern ?? throw new InvalidOperationException($"pattern {_patternId} 不存在");
    }
}
