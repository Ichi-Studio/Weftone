using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Engine;

namespace Weftone.App.Tests;

public class MainWindowViewModelGestureTests
{
    private static MainWindowViewModel NewViewModel(out EditSession session, out Pattern pattern)
    {
        session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
        ProjectDocument document = session.Document;
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        pattern = document.Patterns[0];
        var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        var vm = new MainWindowViewModel(session, playback, new FakeDialogs())
        {
            SelectedPattern = pattern,
        };
        return vm;
    }

    [Test]
    public void Add_note_gesture_merges_add_and_resize_into_single_undo()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out Pattern pattern);
        vm.BeginAddNote(96, 60);
        Note? note = vm.LastAddedNote;
        Assert.That(note, Is.Not.Null);
        vm.DragNoteLength(note!, 192);
        vm.EndNoteGesture();

        Assert.Multiple(() =>
        {
            Assert.That(pattern.Notes.Count, Is.EqualTo(1));
            Assert.That(note!.Tick, Is.EqualTo(96));
            Assert.That(note.LengthTicks, Is.EqualTo(192));
            Assert.That(session.IsDirty, Is.True);
        });
        vm.UndoCommand.Execute(null);
        Assert.That(pattern.Notes.Count, Is.EqualTo(0), "画音符+拖长度合并为一步撤销");
        Assert.That(session.History.CanUndo, Is.False);
    }

    [Test]
    public void Move_gesture_single_undo_restores_origin()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out Pattern pattern);
        var note = new Note { ChannelId = 1, Tick = 96, Key = 60, LengthTicks = 96 };
        pattern.Notes.Add(note);

        vm.BeginMoveNote(note);
        vm.DragNoteMove(note, 192, 62);
        vm.DragNoteMove(note, 384, 64);
        vm.EndNoteGesture();
        Assert.Multiple(() =>
        {
            Assert.That(note.Tick, Is.EqualTo(384));
            Assert.That(note.Key, Is.EqualTo(64));
        });

        vm.UndoCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(note.Tick, Is.EqualTo(96), "撤销回到拖拽起点");
            Assert.That(note.Key, Is.EqualTo(60));
            Assert.That(session.History.CanUndo, Is.False, "整次拖拽只有一条撤销记录");
        });
        vm.RedoCommand.Execute(null);
        Assert.That(note.Tick, Is.EqualTo(384));
    }

    [Test]
    public void Resize_gesture_clamps_to_minimum_length()
    {
        MainWindowViewModel vm = NewViewModel(out _, out Pattern pattern);
        vm.BeginAddNote(0, 60);
        Note note = vm.LastAddedNote!;
        vm.EndNoteGesture();

        vm.BeginResizeNote(note);
        vm.DragNoteLength(note, 0);
        vm.EndNoteGesture();
        Assert.That(note.LengthTicks, Is.EqualTo(1), "长度至少 1 tick");
    }

    [Test]
    public void Remove_note_is_immediate_and_undoable()
    {
        MainWindowViewModel vm = NewViewModel(out _, out Pattern pattern);
        vm.BeginAddNote(0, 60);
        Note note = vm.LastAddedNote!;
        vm.EndNoteGesture();

        vm.RemoveNote(note);
        Assert.That(pattern.Notes.Count, Is.EqualTo(0));
        vm.UndoCommand.Execute(null);
        Assert.That(pattern.Notes.Count, Is.EqualTo(1));
    }

    [Test]
    public void Play_without_pattern_reports_status()
    {
        var session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
        var vm = new MainWindowViewModel(
            session, new PlaybackService(() => new FakeBackend(), new PlanRenderer()), new FakeDialogs());
        vm.PlayCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(vm.StatusText, Is.EqualTo("没有可播放的 Pattern"));
        });
    }

    [Test]
    public void Play_compiles_pattern_loop_and_loop_never_auto_stops()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out Pattern pattern);
        vm.BeginAddNote(0, 60);
        vm.EndNoteGesture();
        vm.PlayCommand.Execute(null);

        var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        playback.Play(session.CompilePattern(pattern.Id));
        Assert.Multiple(() =>
        {
            Assert.That(playback.IsLooping, Is.True);
            Assert.That(playback.TotalSamples, Is.GreaterThan(0));
        });
        Assert.That(vm.IsPlaying, Is.True);
        vm.StopCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.False);
    }

    [Test]
    public void BeginAddNote_without_channels_is_noop()
    {
        var session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
        session.Document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        var vm = new MainWindowViewModel(
            session, new PlaybackService(() => new FakeBackend(), new PlanRenderer()), new FakeDialogs())
        {
            SelectedPattern = session.Document.Patterns[0],
        };
        vm.BeginAddNote(0, 60);
        Assert.Multiple(() =>
        {
            Assert.That(vm.LastAddedNote, Is.Null);
            Assert.That(vm.SelectedPattern!.Notes.Count, Is.EqualTo(0));
        });
        vm.EndNoteGesture();
        Assert.That(session.History.CanUndo, Is.False, "空事务不入栈");
    }
}
