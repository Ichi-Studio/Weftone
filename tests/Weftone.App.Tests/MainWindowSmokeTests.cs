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
        using var session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
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
            Assert.That(window.FindControl<MixerPanel>("Mixer"), Is.Not.Null, "Mixer 面板");
            Assert.That(window.FindControl<PianoKeysControl>("Keys"), Is.Not.Null, "键盘侧栏");
            Assert.That(window.FindControl<ScrollViewer>("RollScroll"), Is.Not.Null);
        });
        window.Close();
    }

    [AvaloniaTest]
    public void Piano_roll_renders_pattern_notes_without_crash()
    {
        using var session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
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

    [AvaloniaTest]
    public void Automation_lane_renders_points_without_crash()
    {
        using var session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
        ProjectDocument document = session.Document;
        document.Channels.Add(new Channel { Id = 1, Name = "Sine", Kind = ChannelKind.Sine });
        document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        document.Patterns[0].Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 60, LengthTicks = 96 });
        AutomationLanes.Insert(document.Channels[0].VolumePoints, new AutomationPoint { Tick = 0, Value = 0.1 });
        AutomationLanes.Insert(document.Channels[0].VolumePoints, new AutomationPoint { Tick = 192, Value = 1.4 });
        AutomationLanes.Insert(document.Channels[0].PanPoints, new AutomationPoint { Tick = 96, Value = -0.8 });
        using var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        var viewModel = new MainWindowViewModel(session, playback, new FakeDialogs())
        {
            SelectedPattern = document.Patterns[0],
            IsAutomationVisible = true,
            SelectedAutomationTarget = new MainWindowViewModel.AutomationTargetItem(
                AutomationTarget.Channel(1), AutomationParameter.Volume, "Sine · 音量"),
        };
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        var lane = window.FindControl<AutomationLaneControl>("Lane");
        Assert.Multiple(() =>
        {
            Assert.That(lane, Is.Not.Null, "自动化 lane 自绘控件");
            Assert.That(viewModel.CurrentAutomationLane, Has.Count.EqualTo(2));
            Assert.That(viewModel.CurrentAutomationRange.Min, Is.EqualTo(0.0));
            Assert.That(viewModel.CurrentAutomationRange.Max, Is.EqualTo(2.0));
        });
        window.Close();
    }
}