using NetArchTest.Rules;
using NUnit.Framework;
using Weftone.Abstractions;
using Weftone.Adapters.Audio.Miniaudio;
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
    public void Adapters_do_not_depend_on_Engine_Domain_Application()
    {
        var result = Types.InAssembly(typeof(MiniaudioBackend).Assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Weftone.Domain", "Weftone.Application", "Weftone.Engine",
                "Weftone.App", "Weftone.PluginHost", "Weftone.Ipc",
                "Avalonia", "SkiaSharp")
            .GetResult();
        Assert.That(result.IsSuccessful, Is.True,
            "Adapters 违规依赖:\n" + string.Join("\n", result.FailingTypeNames ?? []));
    }
}
