namespace Weftone.Domain.Commands;

/// <summary>
/// 导入采样 = 注册 AssetRef + 新建 sampler 通道，一步完成一步撤销
/// （spec §工程存取与采样导入：导入后建 sampler 通道）。
/// 解码数据留在 Application 层内存/工作区，Domain 只见引用。
/// </summary>
public sealed class ImportSampleCommand(Channel channel, AssetRef asset) : ICommand
{
    private readonly Channel _channel = channel;
    private readonly AssetRef _asset = asset;

    public string Title => $"导入采样 {_channel.Name}";

    public void Apply(ProjectDocument document)
    {
        if (document.Channels.Any(c => c.Id == _channel.Id))
        {
            throw new InvalidOperationException($"channel id {_channel.Id} 重复");
        }
        if (document.Assets.Any(a => a.Id == _asset.Id))
        {
            throw new InvalidOperationException($"asset id {_asset.Id} 重复");
        }
        if (_channel.Kind != ChannelKind.Sampler || _channel.AssetId != _asset.Id)
        {
            throw new InvalidOperationException("导入命令必须配套 sampler 通道与同 id 资产");
        }
        document.Assets.Add(_asset);
        document.Channels.Add(_channel);
    }

    public void Revert(ProjectDocument document)
    {
        document.Assets.Remove(_asset);
        document.Channels.Remove(_channel);
    }
}
