using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Weftone.Adapters.Audio.Miniaudio;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.App.Services;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Engine;

namespace Weftone.App;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 组合根（AGENTS.md §4：DI 只在组合根）：装配 Application + Adapters + Engine。
            var pluginFactory = new Adapters.Plugins.Clap.ClapInstanceFactory();
            var session = new EditSession(
                new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer(),
                pluginFactory);
            try
            {
                session.NewDemo();
            }
            catch (Exception ex) when (ex is DllNotFoundException or InvalidOperationException or InvalidDataException)
            {
                // native 未构建或解码失败：以空工程启动，用户可手动打开工程
            }

            var playback = new PlaybackService(() => new MiniaudioBackend(), new PlanRenderer(), pluginFactory);
            var pluginCatalog = new PluginParameterCatalog(pluginFactory);
            var recovery = new RecoveryStore();
            MainWindow? window = null;
            var dialogs = new FileDialogService(() => window);
            var viewModel = new MainWindowViewModel(session, playback, dialogs, recovery, pluginCatalog);
            window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                viewModel.ClearRecoveryOnExit();
                playback.Dispose();
                session.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}