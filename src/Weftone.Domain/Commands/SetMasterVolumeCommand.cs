namespace Weftone.Domain.Commands;

/// <summary>
/// 主控音量命令（M1b Mixer master 推子）。显式 from/to：拖拽预览直接改文档，
/// 释放时以 from=拖前值执行，Revert 精确还原（同 MoveNote 模式）。
/// </summary>
public sealed class SetMasterVolumeCommand(double fromVolume, double toVolume) : ICommand
{
    private readonly double _fromVolume = fromVolume;
    private readonly double _toVolume = toVolume;

    public string Title => $"主控音量 {_fromVolume:0.###}→{_toVolume:0.###}";

    public void Apply(ProjectDocument document)
    {
        if (_toVolume is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(toVolume), _toVolume, "音量须在 [0, 2]");
        }
        document.MasterVolume = _toVolume;
    }

    public void Revert(ProjectDocument document)
    {
        document.MasterVolume = _fromVolume;
    }
}
