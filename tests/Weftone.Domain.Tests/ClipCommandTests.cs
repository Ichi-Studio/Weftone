using NUnit.Framework;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Domain.Tests;

public class ClipCommandTests
{
    private static ProjectDocument NewDocument()
    {
        var document = new ProjectDocument();
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        document.Patterns.Add(new Pattern { Id = 11, Name = "P2" });
        return document;
    }

    [Test]
    public void Add_remove_clip_roundtrip()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var clip = new Clip { PatternId = 10, StartTick = 0 };
        history.Execute(new AddClipCommand(clip), document);
        Assert.That(document.Playlist.Count, Is.EqualTo(1));
        history.Execute(new RemoveClipCommand(clip), document);
        Assert.That(document.Playlist.Count, Is.EqualTo(0));
        history.Undo(document);
        Assert.That(document.Playlist.Count, Is.EqualTo(1));
        history.Undo(document);
        Assert.That(document.Playlist.Count, Is.EqualTo(0));
    }

    [Test]
    public void Add_clip_unknown_pattern_rejected()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        Assert.That(() => history.Execute(
            new AddClipCommand(new Clip { PatternId = 99, StartTick = 0 }), document),
            Throws.InvalidOperationException);
        Assert.That(history.CanUndo, Is.False, "失败命令不得入栈");
    }

    [Test]
    public void Move_clip_changes_tick_and_lane()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var clip = new Clip { PatternId = 10, StartTick = 0 };
        document.Playlist.Add(clip);
        history.Execute(new MoveClipCommand(clip, 0, 10, 384, 11), document);
        Assert.Multiple(() =>
        {
            Assert.That(clip.StartTick, Is.EqualTo(384));
            Assert.That(clip.PatternId, Is.EqualTo(11));
        });
        history.Undo(document);
        Assert.Multiple(() =>
        {
            Assert.That(clip.StartTick, Is.EqualTo(0));
            Assert.That(clip.PatternId, Is.EqualTo(10));
        });
    }

    [Test]
    public void Move_clip_rejects_unknown_target_pattern_and_negative_tick()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var clip = new Clip { PatternId = 10, StartTick = 0 };
        document.Playlist.Add(clip);
        Assert.That(() => new MoveClipCommand(clip, 0, 10, 0, 99).Apply(document),
            Throws.InvalidOperationException);
        Assert.That(() => new MoveClipCommand(clip, 0, 10, -1, 10).Apply(document),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(history.CanUndo, Is.False);
    }

    [Test]
    public void Clip_drag_transaction_merges_into_single_undo()
    {
        ProjectDocument document = NewDocument();
        var history = new CommandHistory();
        var clip = new Clip { PatternId = 10, StartTick = 0 };
        document.Playlist.Add(clip);

        history.BeginTransaction("移动 clip");
        history.Execute(new MoveClipCommand(clip, 0, 10, 96, 10), document);
        history.Execute(new MoveClipCommand(clip, 0, 10, 192, 10), document);
        history.Execute(new MoveClipCommand(clip, 0, 10, 384, 11), document);
        history.EndTransaction();

        Assert.Multiple(() =>
        {
            Assert.That(clip.StartTick, Is.EqualTo(384));
            Assert.That(clip.PatternId, Is.EqualTo(11));
        });
        history.Undo(document);
        Assert.Multiple(() =>
        {
            Assert.That(clip.StartTick, Is.EqualTo(0), "单次撤销回到拖拽起点");
            Assert.That(clip.PatternId, Is.EqualTo(10));
            Assert.That(history.CanUndo, Is.False, "整次拖拽只有一条撤销记录");
        });
    }
}
