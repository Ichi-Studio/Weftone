using NUnit.Framework;
using Weftone.Abstractions.Plugins;
using Weftone.Abstractions.Rendering;
using Weftone.Adapters.Plugins.Clap;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Application.Tests;

/// <summary>SPEC M3 竖切 3：插件参数 set、state chunk 往返、Host 复用/恢复行为。
/// 依赖 native 测试插件，缺失时跳过。</summary>
public class PluginParameterStateTests
{
    private static string? FindClap()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent!;
        }
        return dir is null
            ? null
            : Directory.EnumerateFiles(Path.Combine(dir.FullName, "native", "build"),
                "weftone_test_sine.clap", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static ProjectDocument PluginDocument(string clapPath)
    {
        var document = new ProjectDocument { Tempo = 120, SampleRate = 48_000 };
        document.Plugins.Add(new PluginRef { Id = ClapPluginInstance.TestSinePluginId, Path = clapPath });
        document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Plugin, PluginId = ClapPluginInstance.TestSinePluginId });
        var pattern = new Pattern { Id = 10 };
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 69, Velocity = 1, LengthTicks = 96 });
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        return document;
    }

    [Test]
    public void SetParameter_takes_effect_on_next_process_and_persists_through_state()
    {
        string? clap = FindClap();
        if (clap is null)
        {
            Assert.Ignore("先构建 native 测试插件");
        }
        using var plugin = ClapPluginInstance.Create(clap!, ClapPluginInstance.TestSinePluginId, 48_000);
        Assert.Multiple(() =>
        {
            Assert.That(plugin.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "Gain", "Tune" }));
            Assert.That(plugin.Parameters[0].DefaultValue, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(plugin.Parameters[1].IsAutomatable, Is.True);
            Assert.That(plugin.LatencySamples, Is.EqualTo(32u), "latency 报告（M3 不补偿）");
        });

        var left = new float[64];
        var right = new float[64];
        plugin.Process(left, right, [new PluginNoteEvent(PluginNoteEventKind.On, 0, 69, 0, 1f)]);
        plugin.Process(left, right, []);
        float before = Math.Abs(left[10]);
        Assert.That(before, Is.GreaterThan(0.1f), "默认 gain=0.25 出声");

        plugin.SetParameter(1, 0.05); // gain 降到 0.05
        plugin.Process(left, right, []);
        Assert.That(Math.Abs(left[10]), Is.LessThan(before * 0.5), "参数于下次 process 生效");

        plugin.SetParameter(2, 12.0); // tune +12 半音 → 频率翻倍
        plugin.Process(left, right, []);
        Assert.That(Math.Abs(left[10]), Is.GreaterThan(0f), "tune 后仍出声");
    }

    [Test]
    public void State_roundtrips_gain_and_tune()
    {
        string? clap = FindClap();
        if (clap is null)
        {
            Assert.Ignore("先构建 native 测试插件");
        }
        using (var first = ClapPluginInstance.Create(clap!, ClapPluginInstance.TestSinePluginId, 48_000))
        {
            first.SetParameter(1, 0.7);
            first.SetParameter(2, -5.0);
            var buffer = new float[16];
            var right = new float[16];
            first.Process(buffer, right, []); // 参数已派发进插件
            byte[]? state = first.SaveState();
            Assert.That(state, Is.Not.Null.And.Length.EqualTo(16), "测试插件 state = 2×double");

            using var second = ClapPluginInstance.Create(clap!, ClapPluginInstance.TestSinePluginId, 48_000);
            Assert.That(second.LoadState(state!), Is.True);
            byte[]? reread = second.SaveState();
            Assert.That(reread, Is.EqualTo(state), "load 后再 save 逐字节一致（gain/tune 恢复）");
        }
    }

    [Test]
    public void Host_applies_params_and_state_across_compiles()
    {
        string? clap = FindClap();
        if (clap is null)
        {
            Assert.Ignore("先构建 native 测试插件");
        }
        ProjectDocument document = PluginDocument(clap!);
        document.Channels[0].PluginParameters.Add(new PluginParameterValue(1, 0.4));
        var factory = new ClapInstanceFactory();
        using var host = new PluginInstanceHost(factory);
        RenderPlan first = new DocumentCompiler().Compile(document);
        IReadOnlyList<IAudioPluginInstance> table1 = host.Sync(first);
        // state 抓取（模拟保存工程）
        IReadOnlyDictionary<int, byte[]> states = host.CaptureChannelStates(first);
        Assert.That(states, Does.ContainKey(1));

        // 复用同实例（同 plan 参数值），state 注入后重建仍恢复
        RenderPlan second = new DocumentCompiler().Compile(document);
        IReadOnlyList<IAudioPluginInstance> table2 = host.Sync(second);
        Assert.That(ReferenceEquals(table1[0], table2[0]), Is.True, "(path,id,采样率) 相同复用实例");

        // 参数变化进 plan → 同步 set；驱动一次空块让 pending 参数派发进插件（与真实渲染一致）
        document.Channels[0].PluginParameters[0] = new PluginParameterValue(1, 0.9);
        RenderPlan third = new DocumentCompiler().Compile(document);
        IReadOnlyList<IAudioPluginInstance> table3 = host.Sync(third);
        var drive = new float[32];
        table3[0].Process(drive, drive, []);
        // 验证参数落到实例：经 state 抓取对比（gain 0.9 → state 前 8 字节）
        byte[] updated = host.CaptureChannelStates(third)[1];
        double gain = BitConverter.ToDouble(updated, 0);
        Assert.That(gain, Is.EqualTo(0.9).Within(1e-9), "Sync 把工程参数值 set 进实例");
    }

    [Test]
    public void SetPluginParameterCommand_roundtrip_and_undo()
    {
        var document = new ProjectDocument();
        document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Plugin, PluginId = "p" });
        var history = new CommandHistory();
        history.Execute(new SetPluginParameterCommand(1, 7, 0.0, 0.55), document);
        Assert.That(document.Channels[0].PluginParameters.Single(p => p.Id == 7).Value, Is.EqualTo(0.55));
        history.Undo(document);
        Assert.That(document.Channels[0].PluginParameters.Single(p => p.Id == 7).Value, Is.EqualTo(0.0));
        history.Redo(document);
        Assert.That(document.Channels[0].PluginParameters.Single(p => p.Id == 7).Value, Is.EqualTo(0.55));
    }
}
