using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Engine;

namespace Weftone.App.Tests;

public class MainWindowViewModelMixerTests
{
    private static string TempWavPath()
    {
        string path = Path.Combine(Path.GetTempPath(), "weftone-vm-import-" + Guid.NewGuid().ToString("N") + ".wav");
        DemoProject.WriteKickWav(path);
        return path;
    }

    private static MainWindowViewModel NewViewModel(out EditSession session, FakeDialogs dialogs)
    {
        session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
        session.Document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        session.Document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        var vm = new MainWindowViewModel(session, playback, dialogs)
        {
            SelectedPattern = session.Document.Patterns[0],
        };
        return vm;
    }

    [Test]
    public void ImportSampleAsync_imports_and_notifies_channels()
    {
        string wav = TempWavPath();
        try
        {
            var dialogs = new FakeDialogs { WavToOpen = wav };
            MainWindowViewModel vm = NewViewModel(out EditSession session, dialogs);
            int channelsBefore = vm.Channels.Count;

            vm.ImportSampleCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(vm.Channels.Count, Is.EqualTo(channelsBefore + 1), "通道快照刷新");
                Assert.That(vm.StatusText, Does.StartWith("已导入采样"));
                Assert.That(session.IsDirty, Is.True);
            });
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [Test]
    public void ImportSampleAsync_cancelled_is_noop()
    {
        var dialogs = new FakeDialogs { WavToOpen = null };
        MainWindowViewModel vm = NewViewModel(out _, dialogs);
        int channels = vm.Channels.Count;
        vm.ImportSampleCommand.Execute(null);
        Assert.That(vm.Channels.Count, Is.EqualTo(channels));
    }

    [Test]
    public void ExportAsync_writes_wav_file()
    {
        string outWav = Path.Combine(Path.GetTempPath(), "weftone-vm-export-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var dialogs = new FakeDialogs { WavToSave = outWav };
            MainWindowViewModel vm = NewViewModel(out EditSession session, dialogs);
            session.Document.Patterns[0].Notes.Add(
                new Note { ChannelId = 1, Tick = 0, Key = 60, Velocity = 1.0, LengthTicks = 96 });
            session.Document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });

            vm.ExportCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(outWav), Is.True);
                Assert.That(new FileInfo(outWav).Length, Is.GreaterThan(44));
                Assert.That(vm.StatusText, Does.StartWith("已导出"));
            });
        }
        finally
        {
            File.Delete(outWav);
        }
    }

    [Test]
    public void Mix_drag_is_single_undo_and_live_updates_document()
    {
        var dialogs = new FakeDialogs();
        MainWindowViewModel vm = NewViewModel(out EditSession session, dialogs);
        Channel channel = session.Document.Channels[0];

        vm.BeginMixEdit(channel);
        vm.DragMixEdit(channel, 1.5, -0.5);
        vm.DragMixEdit(channel, 0.8, 0.25);
        Assert.Multiple(() =>
        {
            Assert.That(channel.Volume, Is.EqualTo(0.8), "拖动中实时改文档");
            Assert.That(session.History.CanUndo, Is.False, "命令尚未入栈");
        });
        vm.EndMixEdit(channel);
        Assert.Multiple(() =>
        {
            Assert.That(session.History.CanUndo, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo("就绪"));
        });
        vm.UndoCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(channel.Volume, Is.EqualTo(1.0), "单条撤销回到拖前");
            Assert.That(channel.Pan, Is.EqualTo(0.0));
            Assert.That(session.History.CanUndo, Is.False, "整次拖拽只有一条撤销记录");
        });
    }

    [Test]
    public void Master_drag_is_single_undo()
    {
        var dialogs = new FakeDialogs();
        MainWindowViewModel vm = NewViewModel(out EditSession session, dialogs);
        vm.BeginMasterEdit();
        vm.MasterVolume = 0.3;
        vm.EndMasterEdit();
        Assert.That(session.Document.MasterVolume, Is.EqualTo(0.3));
        vm.UndoCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(session.Document.MasterVolume, Is.EqualTo(1.0));
            Assert.That(session.History.CanUndo, Is.False);
        });
    }

    [Test]
    public void Mix_drag_without_change_commits_nothing()
    {
        var dialogs = new FakeDialogs();
        MainWindowViewModel vm = NewViewModel(out EditSession session, dialogs);
        Channel channel = session.Document.Channels[0];
        vm.BeginMixEdit(channel);
        vm.EndMixEdit(channel);
        Assert.That(session.History.CanUndo, Is.False, "无变化不入栈");
    }
}
