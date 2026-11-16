using NUnit.Framework;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.App.Tests;

/// <summary>SPEC M5 竖切 4：效果面板 API——增删/旁路/参数手势/预设（VM 层 headless）。</summary>
public class EffectPanelTests
{
    private static MainWindowViewModel NewViewModel(out EditSession session, out Channel channel)
    {
        session = new EditSession(new Adapters.ProjectStore.WeftProjectStore(),
            new Adapters.Codecs.DrWavDecoder(), new Adapters.Codecs.LinearResampler(),
            new Engine.PlanRenderer());
        channel = new Channel { Id = 1, Name = "Fx", Kind = ChannelKind.Sine };
        session.Document.Channels.Add(channel);
        var playback = new PlaybackService(() => new FakeBackend(), new Engine.PlanRenderer());
        return new MainWindowViewModel(session, playback, new FakeDialogs());
    }

    [Test]
    public void Add_remove_bypass_are_undoable()
    {
        MainWindowViewModel vm = NewViewModel(out _, out Channel channel);
        vm.AddEffect(channel, EffectKind.Eq3Band);
        Assert.That(channel.Effects.Slots, Has.Count.EqualTo(1));

        EffectSlot slot = channel.Effects.Slots[0];
        vm.SetEffectBypass(channel, slot, true);
        Assert.That(slot.Bypassed, Is.True);
        vm.UndoCommand.Execute(null);
        Assert.That(slot.Bypassed, Is.False);

        vm.RemoveEffect(channel, slot);
        Assert.That(channel.Effects.Slots, Is.Empty);
        vm.UndoCommand.Execute(null);
        Assert.That(channel.Effects.Slots, Has.Count.EqualTo(1));
    }

    [Test]
    public void Param_drag_merges_into_single_undo()
    {
        MainWindowViewModel vm = NewViewModel(out _, out Channel channel);
        vm.AddEffect(channel, EffectKind.Eq3Band);
        EffectSlot slot = channel.Effects.Slots[0];

        vm.BeginEffectParamEdit(channel, slot);
        vm.DragEffectParam("low", 3);
        vm.DragEffectParam("low", 6);
        vm.EndEffectParamEdit();
        Assert.That(slot.Parameters["low"], Is.EqualTo(6));

        vm.UndoCommand.Execute(null);
        Assert.That(slot.Parameters["low"], Is.EqualTo(0), "撤销回到手势前值（命令回滚到初值）");
    }

    [Test]
    public void Preset_save_apply_roundtrip()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out Channel channel);
        vm.AddEffect(channel, EffectKind.Reverb);
        channel.Effects.Slots[0].Parameters["decaySeconds"] = 2.5;
        vm.SaveEffectPreset(channel, "MyVerb");
        Assert.That(session.Document.EffectPresets, Does.ContainKey("MyVerb"));

        Channel other = new() { Id = 2, Name = "Other", Kind = ChannelKind.Sine };
        session.Document.Channels.Add(other);
        vm.ApplyEffectPreset(other, "MyVerb");
        Assert.Multiple(() =>
        {
            Assert.That(other.Effects.Slots, Has.Count.EqualTo(1));
            Assert.That(other.Effects.Slots[0].Kind, Is.EqualTo(EffectKind.Reverb));
            Assert.That(other.Effects.Slots[0].Parameters["decaySeconds"], Is.EqualTo(2.5));
            // 应用是拷贝（互不影响）
            other.Effects.Slots[0].Parameters["decaySeconds"] = 5;
            Assert.That(session.Document.EffectPresets["MyVerb"][0].Parameters["decaySeconds"], Is.EqualTo(2.5));
        });
    }
}
