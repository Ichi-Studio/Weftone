using NetArchTest.Rules;
using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Adapters.Audio.Miniaudio;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.Engine;

namespace Weftone.ArchTests;

public class DependencyRuleTests
{
    [Test]
    public void Engine_only_depends_on_Abstractions()
    {
        var result = Types.InAssembly(typeof(SineEngine).Assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Weftone.Domain", "Weftone.Application", "Weftone.Adapters",
                "Weftone.App", "Weftone.PluginHost", "Weftone.Ipc",
                "Avalonia", "SkiaSharp")
            .GetResult();
        Assert.That(result.IsSuccessful, Is.True,
            "Engine 违规依赖:\n" + string.Join("\n", result.FailingTypeNames ?? []));
    }

    [Test]
    public void Abstractions_depend_on_nothing_internal()
    {
        var result = Types.InAssembly(typeof(IAudioBackend).Assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Weftone.Domain", "Weftone.Application", "Weftone.Engine",
                "Weftone.Adapters", "Weftone.App", "Weftone.PluginHost", "Weftone.Ipc")
            .GetResult();
        Assert.That(result.IsSuccessful, Is.True,
            "Abstractions 违规依赖:\n" + string.Join("\n", result.FailingTypeNames ?? []));
    }

    [Test]
    public void Application_depends_only_on_Domain_and_Abstractions()
    {
        var result = Types.InAssembly(typeof(Weftone.Application.DocumentCompiler).Assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Weftone.Engine", "Weftone.Adapters",
                "Weftone.App", "Weftone.PluginHost", "Weftone.Ipc",
                "Avalonia", "SkiaSharp")
            .GetResult();
        Assert.That(result.IsSuccessful, Is.True,
            "Application 违规依赖:\n" + string.Join("\n", result.FailingTypeNames ?? []));
    }

    [Test]
    public void Adapters_depend_only_on_Abstractions_and_Domain()
    {
        // 六边形：适配器需要领域类型实现端口（如 IProjectStore 用 ProjectDocument），
        // 故允许 → Domain；Engine/UI/用例层仍禁止。
        var result = Types.InAssemblies(
                [typeof(MiniaudioBackend).Assembly, typeof(WeftProjectStore).Assembly, typeof(DrWavDecoder).Assembly])
            .ShouldNot().HaveDependencyOnAny(
                "Weftone.Application", "Weftone.Engine",
                "Weftone.App", "Weftone.PluginHost", "Weftone.Ipc",
                "Avalonia", "SkiaSharp")
            .GetResult();
        Assert.That(result.IsSuccessful, Is.True,
            "Adapters 违规依赖:\n" + string.Join("\n", result.FailingTypeNames ?? []));
    }

    [Test]
    public void Domain_depends_on_nothing_external()
    {
        var result = Types.InAssembly(typeof(Weftone.Domain.ProjectDocument).Assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Weftone.Application", "Weftone.Engine", "Weftone.Adapters",
                "Weftone.App", "Weftone.PluginHost", "Weftone.Ipc",
                "Avalonia", "SkiaSharp")
            .GetResult();
        Assert.That(result.IsSuccessful, Is.True,
            "Domain 违规依赖:\n" + string.Join("\n", result.FailingTypeNames ?? []));
    }
}
