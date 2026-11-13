namespace Weftone.Domain.Commands;

/// <summary>
/// channel 音量/声像合并命令（一次撤销恢复两参数，M1a 粒度）。
/// M1b 改显式 from/to：Mixer 推子拖拽预览直接改文档，释放时以 from=拖前值执行，
/// Revert 精确还原（同 MoveNote 模式）。
/// </summary>
public sealed class SetChannelMixCommand(
    int channelId, double fromVolume, double fromPan, double toVolume, double toPan) : ICommand
{
    private readonly int _channelId = channelId;
    private readonly double _fromVolume = fromVolume;
    private readonly double _fromPan = fromPan;
    private readonly double _toVolume = toVolume;
    private readonly double _toPan = toPan;

    public string Title => $"设置混音 ch={_channelId} vol={_toVolume:0.###} pan={_toPan:0.###}";

    public void Apply(ProjectDocument document)
    {
        if (_toVolume is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(toVolume), _toVolume, "音量须在 [0, 2]");
        }
        if (_toPan is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(toPan), _toPan, "声像须在 [-1, 1]");
        }
        FindChannel(document).Volume = _toVolume;
        FindChannel(document).Pan = _toPan;
    }

    public void Revert(ProjectDocument document)
    {
        Channel channel = FindChannel(document);
        channel.Volume = _fromVolume;
        channel.Pan = _fromPan;
    }

    private Channel FindChannel(ProjectDocument document)
    {
        Channel? channel = document.Channels.FirstOrDefault(c => c.Id == _channelId);
        return channel ?? throw new InvalidOperationException($"channel {_channelId} 不存在");
    }
}
