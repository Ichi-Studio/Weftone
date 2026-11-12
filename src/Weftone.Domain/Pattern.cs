namespace Weftone.Domain;

public sealed class Pattern
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Note> Notes { get; } = [];
}
