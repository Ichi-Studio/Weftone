using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Weftone.App.Tests.TestAppBuilder))]

namespace Weftone.App.Tests;

/// <summary>headless 测试专用 App：只加载 FluentTheme，不跑组合根（避免 native/音频依赖）。</summary>
public class HeadlessTestApp : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}