using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Engine;

namespace Weftone.App.Tests;

public class MainWindowViewModelTests
{
    [Test]
    public void FormatTime_formats_samples_as_minutes_seconds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MainWindowViewModel.FormatTime(0, 48_000), Is.EqualTo("0:00.000"));
            Assert.That(MainWindowViewModel.FormatTime(48_000, 48_000), Is.EqualTo("0:01.000"));
            Assert.That(MainWindowViewModel.FormatTime(2_880_000, 48_000), Is.EqualTo("1:00.000"));
            Assert.That(MainWindowViewModel.FormatTime(100, 0), Is.EqualTo("0:00.000"));
        });
    }

    [Test]
    public void Play_then_stop_toggles_is_playing()
    {
        using var session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler());
        using var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        var vm = new MainWindowViewModel(session, playback, new FakeDialogs());
        vm.PlayCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.True);
        vm.StopCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.False);
    }

    [Test]
    public void Untitled_project_shows_placeholder_name()
    {
        using var session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler());
        using var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        var vm = new MainWindowViewModel(session, playback, new FakeDialogs());
        Assert.That(vm.Title, Does.Contain("未命名"));
    }
}