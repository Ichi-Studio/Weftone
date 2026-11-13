namespace Weftone.Domain.Commands;

public sealed class AddChannelCommand(Channel channel) : ICommand
{
    private readonly Channel _channel = channel;

    public string Title => $"添加通道 {_channel.Name}({_channel.Kind})";

    public void Apply(ProjectDocument document)
    {
        if (document.Channels.Any(c => c.Id == _channel.Id))
        {
            throw new InvalidOperationException($"channel id {_channel.Id} 重复");
        }
        if (_channel.Kind == ChannelKind.Sampler && string.IsNullOrEmpty(_channel.AssetId))
        {
            throw new InvalidOperationException("sampler 通道必须带 assetId");
        }
        document.Channels.Add(_channel);
    }

    public void Revert(ProjectDocument document)
    {
        document.Channels.Remove(_channel);
    }
}
