namespace Weftone.Domain.Commands;

/// <summary>channel 音量/声像合并命令（一次撤销恢复两参数，M1a 粒度）。</summary>
public sealed class SetChannelMixCommand(int channelId, double volume, double pan) : ICommand
{
    private readonly int _channelId = channelId;
    private readonly double _volume = volume;
    private readonly double _pan = pan;
    private double _oldVolume;
    private double _oldPan;

    public string Title => $"设置混音 ch={_channelId} vol={_volume:0.###} pan={_pan:0.###}";

    public void Apply(ProjectDocument document)
    {
        if (_volume is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), _volume, "音量须在 [0, 2]");
        }
        if (_pan is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pan), _pan, "声像须在 [-1, 1]");
        }
        Channel channel = FindChannel(document);
        _oldVolume = channel.Volume;
        _oldPan = channel.Pan;
        channel.Volume = _volume;
        channel.Pan = _pan;
    }

    public void Revert(ProjectDocument document)
    {
        Channel channel = FindChannel(document);
        channel.Volume = _oldVolume;
        channel.Pan = _oldPan;
    }

    private Channel FindChannel(ProjectDocument document)
    {
        Channel? channel = document.Channels.FirstOrDefault(c => c.Id == _channelId);
        return channel ?? throw new InvalidOperationException($"channel {_channelId} 不存在");
    }
}
