namespace Weftone.Domain.Commands;

public sealed class AddNoteCommand(int patternId, Note note) : ICommand
{
    private readonly int _patternId = patternId;
    private Note _note = note;

    public string Title => $"添加音符 tick={_note.Tick} key={_note.Key}";

    public void Apply(ProjectDocument document)
    {
        Pattern pattern = FindPattern(document);
        if (pattern.Notes.Contains(_note))
        {
            throw new InvalidOperationException("音符已存在于 pattern");
        }
        pattern.Notes.Add(_note);
    }

    public void Revert(ProjectDocument document)
    {
        FindPattern(document).Notes.Remove(_note);
    }

    private Pattern FindPattern(ProjectDocument document)
    {
        Pattern? pattern = document.Patterns.FirstOrDefault(p => p.Id == _patternId);
        return pattern ?? throw new InvalidOperationException($"pattern {_patternId} 不存在");
    }
}
