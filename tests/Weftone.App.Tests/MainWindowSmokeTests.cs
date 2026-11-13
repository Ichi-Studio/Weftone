using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.App.Controls;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Engine;

namespace Weftone.App.Tests;

public class MainWindowSmokeTests
{
    [AvaloniaTest]
    public void MainWindow_opens_with_transport_controls()
    {
        using var session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler());
        using var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        var viewModel = new MainWindowViewModel(session, playback, new FakeDialogs());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Assert.Multiple(() =>
        {
            Assert.That(window.FindControl<Button>("PlayButton"), Is.Not.Null);
            Assert.That(window.FindControl<Button>("StopButton"), Is.Not.Null);
            Assert.That(window.FindControl<ToggleButton>("SongModeToggle"), Is.Not.Null, "Song 模式切换");
            Assert.That(window.FindControl<PlaylistControl>("Playlist"), Is.Not.Null, "Playlist 自绘控件");
            Assert.That(window.FindControl<PianoRollControl>("Roll"), Is.Not.Null, "Piano Roll 自绘控件");
            Assert.That(window.FindControl<PianoKeysControl>("Keys"), Is.Not.Null, "键盘侧栏");
            Assert.That(window.FindControl<ScrollViewer>("RollScroll"), Is.Not.Null);
        });
        window.Close();
    }

    [AvaloniaTest]
    public void Piano_roll_renders_pattern_notes_without_crash()
    {
        using var session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler());
        ProjectDocument document = session.Document;
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        document.Patterns[0].Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 60, LengthTicks = 96 });
        using var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        var viewModel = new MainWindowViewModel(session, playback, new FakeDialogs())
        {
            SelectedPattern = document.Patterns[0],
        };
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        var roll = window.FindControl<PianoRollControl>("Roll");
        Assert.That(roll!.Pattern, Is.SameAs(document.Patterns[0]), "Pattern 经绑定传入自绘控件");
        window.Close();
    }
}