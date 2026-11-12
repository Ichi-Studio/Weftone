using NUnit.Framework;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Domain.Tests;

public class CommandHistoryTests
{
    private static ProjectDocument NewDocument()
    {
        var document = new ProjectDocument();
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        return document;
    }

    [Test]
    public void Tempo_undo_redo_roundtrip()
    {
        var document = NewDocument();
        var history = new CommandHistory();
        history.Execute(new SetTempoCommand(120), document);
        Assert.That(document.Tempo, Is.EqualTo(120));
        history.Undo(document);
        Assert.That(document.Tempo, Is.EqualTo(140));
        history.Redo(document);
        Assert.That(document.Tempo, Is.EqualTo(120));
    }

    [Test]
    public void Empty_stacks_reject()
    {
        var document = NewDocument();
        var history = new CommandHistory();
        Assert.That(() => history.Undo(document), Throws.InvalidOperationException);
        Assert.That(() => history.Redo(document), Throws.InvalidOperationException);
    }

    [Test]
    public void Redo_cleared_after_new_command()
    {
        var document = NewDocument();
        var history = new CommandHistory();
        history.Execute(new SetTempoCommand(120), document);
        history.Undo(document);
        history.Execute(new SetTempoCommand(150), document);
        Assert.That(history.CanRedo, Is.False);
        Assert.That(document.Tempo, Is.EqualTo(150));
    }

    [Test]
    public void Add_remove_note_undo()
    {
        var document = NewDocument();
        var history = new CommandHistory();
        var note = new Note { ChannelId = 1, Tick = 0, Key = 60 };
        history.Execute(new AddNoteCommand(10, note), document);
        Assert.That(document.Patterns[0].Notes.Count, Is.EqualTo(1));
        history.Execute(new RemoveNoteCommand(10, note), document);
        Assert.That(document.Patterns[0].Notes.Count, Is.EqualTo(0));
        history.Undo(document);
        Assert.That(document.Patterns[0].Notes.Count, Is.EqualTo(1));
        history.Undo(document);
        Assert.That(document.Patterns[0].Notes.Count, Is.EqualTo(0));
    }

    [Test]
    public void Channel_mix_undo_restores_both_parameters()
    {
        var document = NewDocument();
        var history = new CommandHistory();
        history.Execute(new SetChannelMixCommand(1, 0.5, -0.25), document);
        Assert.Multiple(() =>
        {
            Assert.That(document.Channels[0].Volume, Is.EqualTo(0.5));
            Assert.That(document.Channels[0].Pan, Is.EqualTo(-0.25));
        });
        history.Undo(document);
        Assert.Multiple(() =>
        {
            Assert.That(document.Channels[0].Volume, Is.EqualTo(1.0));
            Assert.That(document.Channels[0].Pan, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void Out_of_range_values_reject()
    {
        var document = NewDocument();
        var history = new CommandHistory();
        Assert.That(() => history.Execute(new SetTempoCommand(5), document),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(() => history.Execute(new SetChannelMixCommand(1, 3, 0), document),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(history.CanUndo, Is.False, "失败命令不得入栈");
    }
}
