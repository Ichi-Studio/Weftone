using Weftone.Domain;

namespace Weftone.Domain.Commands;

/// <summary>设置采样通道拉伸档位（SPEC M4 D6：六档近邻，from/to 显式，拖拽预览-后提交）。</summary>
public sealed class SetChannelStretchSpeedCommand(int channelId, double fromSpeed, double toSpeed) : ICommand
{
    private readonly int _channelId = channelId;
    private readonly double _fromSpeed = fromSpeed;
    private readonly double _toSpeed = toSpeed;

    public string Title => $"拉伸档位 ch={_channelId}: {_fromSpeed:0.##}→{_toSpeed:0.##}";

    public void Apply(ProjectDocument document) => Find(document).StretchSpeed = _toSpeed;

    public void Revert(ProjectDocument document) => Find(document).StretchSpeed = _fromSpeed;

    private Channel Find(ProjectDocument document)
    {
        Channel channel = document.Channels.FirstOrDefault(c => c.Id == _channelId)
            ?? throw new InvalidOperationException($"channel {_channelId} 不存在");
        if (channel.Kind != ChannelKind.Sampler)
        {
            throw new InvalidOperationException($"channel {_channelId} 不是采样通道");
        }
        if (_toSpeed is < 0.25 or > 4.0)
        {
            throw new ArgumentOutOfRangeException(nameof(toSpeed), _toSpeed, "拉伸档位须在 [0.25, 4]");
        }
        return channel;
    }
}

/// <summary>开关切片演奏（SPEC M4 D5：通道级；开启时与拉伸档位互斥——拉伸会使切片帧域失效）。</summary>
public sealed class SetChannelSliceModeCommand(int channelId, bool fromMode, bool toMode) : ICommand
{
    private readonly int _channelId = channelId;
    private readonly bool _fromMode = fromMode;
    private readonly bool _toMode = toMode;

    public string Title => $"切片模式 ch={_channelId}: {_fromMode}→{_toMode}";

    public void Apply(ProjectDocument document)
    {
        Channel channel = Find(document);
        channel.SliceMode = _toMode;
        if (_toMode)
        {
            channel.StretchSpeed = 1.0; // 互斥：切片帧域与拉伸变体不同源
        }
    }

    public void Revert(ProjectDocument document)
    {
        Channel channel = Find(document);
        channel.SliceMode = _fromMode;
    }

    private Channel Find(ProjectDocument document)
    {
        Channel channel = document.Channels.FirstOrDefault(c => c.Id == _channelId)
            ?? throw new InvalidOperationException($"channel {_channelId} 不存在");
        if (channel.Kind != ChannelKind.Sampler)
        {
            throw new InvalidOperationException($"channel {_channelId} 不是采样通道");
        }
        return channel;
    }
}
