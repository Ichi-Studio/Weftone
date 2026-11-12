namespace Weftone.Domain.Commands;

public sealed class SetTempoCommand(int newTempo) : ICommand
{
    private readonly int _newTempo = newTempo;
    private int _oldTempo;

    public string Title => $"设置 BPM={_newTempo}";

    public void Apply(ProjectDocument document)
    {
        if (_newTempo < ProjectDocument.MinTempo || _newTempo > ProjectDocument.MaxTempo)
        {
            throw new ArgumentOutOfRangeException(nameof(newTempo), _newTempo,
                $"BPM 须在 [{ProjectDocument.MinTempo}, {ProjectDocument.MaxTempo}]");
        }
        _oldTempo = document.Tempo;
        document.Tempo = _newTempo;
    }

    public void Revert(ProjectDocument document) => document.Tempo = _oldTempo;
}
