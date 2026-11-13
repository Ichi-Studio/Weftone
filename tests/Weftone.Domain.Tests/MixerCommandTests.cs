using NUnit.Framework;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Domain.Tests;

public class MixerCommandTests
{
    private static ProjectDocument NewDocument()
    {
        var document = new ProjectDocument();
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        return document;
    }

    [Test]
    public void Add_channel_roundtrip_and_duplicate_id_rejected()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var channel = new Channel { Id = 2, Name = "Sine 2", Kind = ChannelKind.Sine };
        history.Execute(new AddChannelCommand(channel), document);
        Assert.That(document.Channels.Count, Is.EqualTo(2));
        history.Undo(document);
        Assert.That(document.Channels.Count, Is.EqualTo(1));
        Assert.That(() => history.Execute(
            new AddChannelCommand(new Channel { Id = 1, Name = "Dup", Kind = ChannelKind.Sine }), document),
            Throws.InvalidOperationException);
        Assert.That(history.CanRedo, Is.True, "失败命令不影响栈");
    }

    [Test]
    public void Add_sampler_channel_requires_asset_id()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        Assert.That(() => history.Execute(
            new AddChannelCommand(new Channel { Id = 2, Name = "S", Kind = ChannelKind.Sampler }), document),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Import_sample_adds_channel_and_asset_as_one_undo()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var asset = new AssetRef { Id = "asset-x", Path = "assets/x.wav" };
        var channel = new Channel { Id = 2, Name = "Kick", Kind = ChannelKind.Sampler, AssetId = "asset-x" };
        history.Execute(new ImportSampleCommand(channel, asset), document);
        Assert.Multiple(() =>
        {
            Assert.That(document.Channels.Count, Is.EqualTo(2));
            Assert.That(document.Assets.Count, Is.EqualTo(1));
        });
        history.Undo(document);
        Assert.Multiple(() =>
        {
            Assert.That(document.Channels.Count, Is.EqualTo(1), "通道与资产一步撤销");
            Assert.That(document.Assets.Count, Is.EqualTo(0));
        });
        history.Redo(document);
        Assert.Multiple(() =>
        {
            Assert.That(document.Channels.Count, Is.EqualTo(2));
            Assert.That(document.Assets.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Import_sample_mismatched_pair_rejected()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var asset = new AssetRef { Id = "asset-x", Path = "assets/x.wav" };
        Assert.That(() => history.Execute(new ImportSampleCommand(
            new Channel { Id = 2, Name = "Sine", Kind = ChannelKind.Sine }, asset), document),
            Throws.InvalidOperationException, "导入命令必须配套 sampler 通道");
        Assert.That(() => history.Execute(new ImportSampleCommand(
            new Channel { Id = 2, Name = "K", Kind = ChannelKind.Sampler, AssetId = "asset-other" }, asset), document),
            Throws.InvalidOperationException, "assetId 必须与资产一致");
    }

    [Test]
    public void Set_master_volume_roundtrip_and_range()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        history.Execute(new SetMasterVolumeCommand(1.0, 0.5), document);
        Assert.That(document.MasterVolume, Is.EqualTo(0.5));
        history.Undo(document);
        Assert.That(document.MasterVolume, Is.EqualTo(1.0));
        Assert.That(() => new SetMasterVolumeCommand(1.0, 2.5).Apply(document),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
