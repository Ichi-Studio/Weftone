using Weftone.Domain;

namespace Weftone.Domain.Commands;

/// <summary>设置插件通道参数（SPEC M3 竖切 3）：from/to 显式，拖拽预览-后提交（同混音推子模式）。</summary>
public sealed class SetPluginParameterCommand(
    int channelId, uint parameterId, double fromValue, double toValue) : ICommand
{
    private readonly int _channelId = channelId;
    private readonly uint _parameterId = parameterId;
    private readonly double _fromValue = fromValue;
    private readonly double _toValue = toValue;

    public string Title => $"插件参数 ch={_channelId} id={_parameterId}: {_fromValue:0.###}→{_toValue:0.###}";

    public void Apply(ProjectDocument document)
    {
        Find(document, _parameterId).Value = _toValue;
    }

    public void Revert(ProjectDocument document)
    {
        Find(document, _parameterId).Value = _fromValue;
    }

    private PluginParameterValue Find(ProjectDocument document, uint parameterId)
    {
        Channel channel = document.Channels.FirstOrDefault(c => c.Id == _channelId)
            ?? throw new InvalidOperationException($"channel {_channelId} 不存在");
        if (channel.Kind != ChannelKind.Plugin)
        {
            throw new InvalidOperationException($"channel {_channelId} 不是插件通道");
        }
        PluginParameterValue? value = channel.PluginParameters.FirstOrDefault(p => p.Id == parameterId);
        if (value is not null)
        {
            return value;
        }
        value = new PluginParameterValue(parameterId, 0);
        channel.PluginParameters.Add(value);
        return value;
    }
}
