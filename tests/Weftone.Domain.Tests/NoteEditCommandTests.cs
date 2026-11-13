using NUnit.Framework;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Domain.Tests;

public class NoteEditCommandTests
{
    private static ProjectDocument NewDocument()
    {
        var document = new ProjectDocument();
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        return document;
    }

    [Test]
    public void MoveNote_apply_revert_roundtrip()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var note = new Note { ChannelId = 1, Tick = 0, Key = 60 };
        document.Patterns[0].Notes.Add(note);
        history.Execute(new MoveNoteCommand(10, note, 0, 60, 192, 64), document);
        Assert.Multiple(() =>
        {
            Assert.That(note.Tick, Is.EqualTo(192));
            Assert.That(note.Key, Is.EqualTo(64));
        });
        history.Undo(document);
        Assert.Multiple(() =>
        {
            Assert.That(note.Tick, Is.EqualTo(0));
            Assert.That(note.Key, Is.EqualTo(60));
        });
        history.Redo(document);
        Assert.That(note.Tick, Is.EqualTo(192));
    }

    [Test]
    public void ResizeNote_apply_revert_and_min_length()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var note = new Note { ChannelId = 1, Tick = 0, Key = 60, LengthTicks = 96 };
        document.Patterns[0].Notes.Add(note);
        history.Execute(new ResizeNoteCommand(10, note, 96, 192), document);
        Assert.That(note.LengthTicks, Is.EqualTo(192));
        history.Undo(document);
        Assert.That(note.LengthTicks, Is.EqualTo(96));
        Assert.That(() => new ResizeNoteCommand(10, note, 96, 0).Apply(document),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(history.CanRedo, Is.True, "失败命令不得入栈（既有记录不受影响）");
    }

    [Test]
    public void Transaction_merges_drag_steps_into_single_undo()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var note = new Note { ChannelId = 1, Tick = 0, Key = 60 };
        document.Patterns[0].Notes.Add(note);

        history.BeginTransaction("移动音符");
        history.Execute(new MoveNoteCommand(10, note, 0, 60, 24, 61), document);
        history.Execute(new MoveNoteCommand(10, note, 0, 60, 48, 62), document);
        history.Execute(new MoveNoteCommand(10, note, 0, 60, 96, 64), document);
        Assert.That(history.CanUndo, Is.False, "事务进行中命令未入栈");
        history.EndTransaction();

        Assert.Multiple(() =>
        {
            Assert.That(history.CanUndo, Is.True);
            Assert.That(note.Tick, Is.EqualTo(96));
            Assert.That(note.Key, Is.EqualTo(64));
        });
        history.Undo(document);
        Assert.Multiple(() =>
        {
            Assert.That(note.Tick, Is.EqualTo(0), "单次撤销回到手势起点");
            Assert.That(note.Key, Is.EqualTo(60));
            Assert.That(history.CanUndo, Is.False, "整次拖拽只有一条撤销记录");
        });
        history.Redo(document);
        Assert.That(note.Tick, Is.EqualTo(96), "重做回到手势终点");
    }

    [Test]
    public void Transaction_add_then_resize_single_undo_removes_note()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        history.BeginTransaction("添加音符");
        var note = new Note { ChannelId = 1, Tick = 96, Key = 60, LengthTicks = 96 };
        history.Execute(new AddNoteCommand(10, note), document);
        history.Execute(new ResizeNoteCommand(10, note, 96, 192), document);
        history.EndTransaction();
        Assert.That(document.Patterns[0].Notes.Count, Is.EqualTo(1));
        Assert.That(note.LengthTicks, Is.EqualTo(192));
        history.Undo(document);
        Assert.Multiple(() =>
        {
            Assert.That(document.Patterns[0].Notes.Count, Is.EqualTo(0), "画音符+拖长度合并为一步撤销");
            Assert.That(history.CanRedo, Is.True);
        });
    }

    [Test]
    public void Transaction_guards()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        Assert.That(() => history.EndTransaction(), Throws.InvalidOperationException);
        history.BeginTransaction("t");
        Assert.That(() => history.BeginTransaction("t2"), Throws.InvalidOperationException);
        Assert.That(() => history.Undo(document), Throws.InvalidOperationException, "事务中禁撤销");
        Assert.That(() => history.Redo(document), Throws.InvalidOperationException, "事务中禁重做");
        history.EndTransaction();
    }

    [Test]
    public void Empty_transaction_pushes_nothing()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        history.BeginTransaction("空");
        history.EndTransaction();
        Assert.That(history.CanUndo, Is.False);
    }

    [Test]
    public void Redo_cleared_after_transaction_command()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        history.Execute(new SetTempoCommand(120), document);
        history.Undo(document);
        Assert.That(history.CanRedo, Is.True);
        history.BeginTransaction("移动音符");
        var note = new Note { ChannelId = 1, Tick = 0, Key = 60 };
        document.Patterns[0].Notes.Add(note);
        history.Execute(new MoveNoteCommand(10, note, 0, 60, 96, 60), document);
        history.EndTransaction();
        Assert.That(history.CanRedo, Is.False, "新编辑（含事务）清空重做栈");
    }
}
