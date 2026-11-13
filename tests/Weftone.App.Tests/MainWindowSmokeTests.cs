using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.App.ViewModels;
using Weftone.Application;
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
        });
        window.Close();
    }
}