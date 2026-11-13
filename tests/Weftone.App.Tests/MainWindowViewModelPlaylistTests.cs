using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Engine;

namespace Weftone.App.Tests;

public class MainWindowViewModelPlaylistTests
{
    private static MainWindowViewModel NewViewModel(out EditSession session, out ProjectDocument document)
    {
        session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
        document = session.Document;
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        document.Patterns.Add(new Pattern { Id = 11, Name = "P2" });
        document.Patterns[0].Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 60, LengthTicks = 96 });
        document.Patterns[1].Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 64, LengthTicks = 96 });
        var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        var vm = new MainWindowViewModel(session, playback, new FakeDialogs())
        {
            SelectedPattern = document.Patterns[0],
        };
        return vm;
    }

    [Test]
    public void Add_clip_then_undo_removes_it()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out ProjectDocument document);
        vm.AddClipAt(document.Patterns[1], 384);
        Assert.Multiple(() =>
        {
            Assert.That(document.Playlist.Count, Is.EqualTo(1));
            Assert.That(vm.LastAddedClip, Is.Not.Null);
            Assert.That(session.IsDirty, Is.True);
        });
        vm.UndoCommand.Execute(null);
        Assert.That(document.Playlist.Count, Is.EqualTo(0));
    }

    [Test]
    public void Clip_drag_merges_into_single_undo_and_can_change_lane()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out ProjectDocument document);
        var clip = new Clip { PatternId = 10, StartTick = 0 };
        document.Playlist.Add(clip);

        vm.BeginMoveClip(clip);
        vm.DragClip(clip, 96, document.Patterns[0]);
        vm.DragClip(clip, 384, document.Patterns[1]);
        vm.EndClipGesture();
        Assert.Multiple(() =>
        {
            Assert.That(clip.StartTick, Is.EqualTo(384));
            Assert.That(clip.PatternId, Is.EqualTo(11), "拖到第二轨换 Pattern");
        });

        vm.UndoCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(clip.StartTick, Is.EqualTo(0));
            Assert.That(clip.PatternId, Is.EqualTo(10));
            Assert.That(session.History.CanUndo, Is.False, "整次拖拽只有一条撤销记录");
        });
    }

    [Test]
    public void Remove_clip_is_immediate_and_undoable()
    {
        MainWindowViewModel vm = NewViewModel(out _, out ProjectDocument document);
        var clip = new Clip { PatternId = 10, StartTick = 0 };
        document.Playlist.Add(clip);
        vm.RemoveClip(clip);
        Assert.That(document.Playlist.Count, Is.EqualTo(0));
        vm.UndoCommand.Execute(null);
        Assert.That(document.Playlist.Count, Is.EqualTo(1));
    }

    [Test]
    public void Song_mode_switches_status_while_playing()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out ProjectDocument document);
        vm.PlayCommand.Execute(null);
        Assert.That(vm.StatusText, Is.EqualTo("Pattern 循环播放中"));
        vm.IsSongMode = true;
        Assert.That(vm.StatusText, Is.EqualTo("Song 播放中"), "播放中切模式即换计划");
        vm.IsSongMode = false;
        Assert.That(vm.StatusText, Is.EqualTo("Pattern 循环播放中"));
        vm.StopCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.False);
    }

    [Test]
    public void Song_compile_is_linear_and_pattern_compile_loops()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out ProjectDocument document);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        document.Playlist.Add(new Clip { PatternId = 11, StartTick = 384 });

        var patternPlayback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        patternPlayback.Play(session.CompilePattern(10));
        var songPlayback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        songPlayback.Play(session.Compile());
        Assert.Multiple(() =>
        {
            Assert.That(patternPlayback.IsLooping, Is.True, "Pattern 计划循环");
            Assert.That(songPlayback.IsLooping, Is.False, "Song 计划线性");
            Assert.That(songPlayback.TotalSamples,
                Is.GreaterThan(patternPlayback.TotalSamples), "两段编排长于单 pattern");
        });
    }

    [Test]
    public void DocumentEdited_fires_on_edit_and_undo()
    {
        MainWindowViewModel vm = NewViewModel(out _, out ProjectDocument document);
        int fired = 0;
        vm.DocumentEdited += () => fired++;
        vm.AddClipAt(document.Patterns[0], 0);
        int afterAdd = fired;
        vm.UndoCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(afterAdd, Is.GreaterThan(0), "编辑后触发");
            Assert.That(fired, Is.GreaterThan(afterAdd), "撤销后触发");
        });
    }
}
