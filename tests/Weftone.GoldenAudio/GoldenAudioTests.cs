using System.Runtime.InteropServices;
using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.Plugins.Clap;
using Weftone.Adapters.ProjectStore;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Engine;

namespace Weftone.GoldenAudio;

/// <summary>
/// 黄金音频回归（AGENTS.md §10）：DemoProject 全链渲染
/// 同进程逐位一致；对提交基线跨平台峰值差 ≤ 1e-4。
/// 更新基线：本地设 WEFTONE_GOLDEN_UPDATE=1 跑本测试后提交 baselines/。
/// </summary>
public class GoldenAudioTests
{
    private string _workDir = "";

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "weftone-golden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_workDir, recursive: true);

    private ProjectRenderService NewService() => new(
        new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer(),
        stretcher: new Adapters.Dsp.SignalsmithStretcher());

    private string CreateGoldenPackage()
    {
        string kickPath = Path.Combine(_workDir, "kick.wav");
        DemoProject.WriteKickWav(kickPath);
        string package = Path.Combine(_workDir, "golden.weft");
        new WeftProjectStore().Save(package, DemoProject.CreateDocument(),
            new Dictionary<string, string> { ["assets/kick.wav"] = kickPath });
        return package;
    }

    private static string BaselinePath
    {
        get
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                dir = dir.Parent!;
            }
            string root = dir?.FullName ?? TestContext.CurrentContext.TestDirectory;
            return Path.Combine(root, "tests", "Weftone.GoldenAudio", "baselines", "m1a-golden.wav");
        }
    }

    /// <summary>M2 自动化黄金基线路径：volume 斜坡 + pan 摆动 + master 渐变（SPEC M2 §测试与 CI）。</summary>
    private static string AutomationBaselinePath
    {
        get
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                dir = dir.Parent!;
            }
            string root = dir?.FullName ?? TestContext.CurrentContext.TestDirectory;
            return Path.Combine(root, "tests", "Weftone.GoldenAudio", "baselines", "m2-automation-golden.wav");
        }
    }

    /// <summary>Demo + 自动化：Lead 通道 volume 0.1→1.3 斜坡、pan -1→+1 摆动、master 1.0→0.2 渐变；
    /// Kick 通道保持静态（混合路径回归）。时间轴 960 tick（两段 clip）。</summary>
    private static ProjectDocument CreateAutomationDocument()
    {
        ProjectDocument document = DemoProject.CreateDocument();
        Channel lead = document.Channels[0];
        AutomationLanes.Insert(lead.VolumePoints, new AutomationPoint { Tick = 0, Value = 0.1 });
        AutomationLanes.Insert(lead.VolumePoints, new AutomationPoint { Tick = 960, Value = 1.3 });
        AutomationLanes.Insert(lead.PanPoints, new AutomationPoint { Tick = 0, Value = -1.0 });
        AutomationLanes.Insert(lead.PanPoints, new AutomationPoint { Tick = 480, Value = 1.0 });
        AutomationLanes.Insert(document.MasterVolumePoints, new AutomationPoint { Tick = 0, Value = 1.0 });
        AutomationLanes.Insert(document.MasterVolumePoints, new AutomationPoint { Tick = 960, Value = 0.2 });
        return document;
    }

    private string CreateAutomationPackage()
    {
        string kickPath = Path.Combine(_workDir, "kick.wav");
        DemoProject.WriteKickWav(kickPath);
        string package = Path.Combine(_workDir, "m2-golden.weft");
        new WeftProjectStore().Save(package, CreateAutomationDocument(),
            new Dictionary<string, string> { ["assets/kick.wav"] = kickPath });
        return package;
    }

    [Test]
    public void Automation_render_is_bitwise_deterministic_in_process()
    {
        string package = CreateAutomationPackage();
        RenderedProject first = NewService().Render(package);
        RenderedProject second = NewService().Render(package);
        Assert.Multiple(() =>
        {
            Assert.That(second.Frames, Is.EqualTo(first.Frames));
            Assert.That(second.Interleaved, Is.EqualTo(first.Interleaved), "自动化斜坡必须逐位确定");
        });
    }

    [Test]
    public void Automation_render_matches_baseline_within_cross_platform_tolerance()
    {
        string package = CreateAutomationPackage();
        RenderedProject rendered = NewService().Render(package);

        if (Environment.GetEnvironmentVariable("WEFTONE_GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AutomationBaselinePath)!);
            using FileStream stream = File.Create(AutomationBaselinePath);
            WavWriter.WriteHeader(stream, rendered.SampleRate, rendered.Channels, rendered.Interleaved.Length);
            WavWriter.WriteSamples(stream, rendered.Interleaved);
            Assert.Warn($"基线已更新: {AutomationBaselinePath}（请提交）");
            return;
        }

        Assert.That(File.Exists(AutomationBaselinePath), Is.True,
            $"缺少基线 {AutomationBaselinePath}；本地设 WEFTONE_GOLDEN_UPDATE=1 生成后提交");
        float[] baseline = ReadFloatWav(AutomationBaselinePath);
        Assert.That(baseline.Length, Is.EqualTo(rendered.Interleaved.Length), "样本数与基线不一致");

        double peakDifference = 0;
        for (int i = 0; i < baseline.Length; i++)
        {
            double difference = Math.Abs(baseline[i] - rendered.Interleaved[i]);
            if (difference > peakDifference)
            {
                peakDifference = difference;
            }
        }
        TestContext.Out.WriteLine($"自动化黄金对比: 样本 {baseline.Length}，峰值差 {peakDifference:0.0e0}");
        Assert.That(peakDifference, Is.LessThanOrEqualTo(1e-4), "自动化黄金音频回归超差");
    }

    [Test]
    public void Render_is_bitwise_deterministic_in_process()
    {
        string package = CreateGoldenPackage();
        RenderedProject first = NewService().Render(package);
        RenderedProject second = NewService().Render(package);
        Assert.Multiple(() =>
        {
            Assert.That(second.Frames, Is.EqualTo(first.Frames));
            Assert.That(second.Interleaved, Is.EqualTo(first.Interleaved), "同工程两次渲染必须逐位一致");
        });
    }

    [Test]
    public void Golden_render_matches_baseline_within_cross_platform_tolerance()
    {
        string package = CreateGoldenPackage();
        RenderedProject rendered = NewService().Render(package);

        if (Environment.GetEnvironmentVariable("WEFTONE_GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
            using FileStream stream = File.Create(BaselinePath);
            WavWriter.WriteHeader(stream, rendered.SampleRate, rendered.Channels, rendered.Interleaved.Length);
            WavWriter.WriteSamples(stream, rendered.Interleaved);
            Assert.Warn($"基线已更新: {BaselinePath}（请提交）");
            return;
        }

        Assert.That(File.Exists(BaselinePath), Is.True,
            $"缺少基线 {BaselinePath}；本地设 WEFTONE_GOLDEN_UPDATE=1 生成后提交");
        float[] baseline = ReadFloatWav(BaselinePath);
        Assert.That(baseline.Length, Is.EqualTo(rendered.Interleaved.Length),
            "样本数与基线不一致（渲染输出形状变化需人工确认并更新基线）");

        double peakDifference = 0;
        for (int i = 0; i < baseline.Length; i++)
        {
            double difference = Math.Abs(baseline[i] - rendered.Interleaved[i]);
            if (difference > peakDifference)
            {
                peakDifference = difference;
            }
        }
        TestContext.Out.WriteLine($"黄金对比: 样本 {baseline.Length}，峰值差 {peakDifference:0.0e0}");
        Assert.That(peakDifference, Is.LessThanOrEqualTo(1e-4),
            "跨平台黄金音频回归超差（内部 DSP 同机位一致标准为 ≤1e-6，见 AGENTS.md §10）");
    }

    /// <summary>M3 插件黄金基线路径：测试插件全链（.weft → 实例表 → 渲染）。</summary>
    private static string PluginBaselinePath
    {
        get
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                dir = dir.Parent!;
            }
            string root = dir?.FullName ?? TestContext.CurrentContext.TestDirectory;
            return Path.Combine(root, "tests", "Weftone.GoldenAudio", "baselines", "m3-plugin-golden.wav");
        }
    }

    private static string? FindTestSineClap()
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

    /// <summary>插件工程：test_sine 通道奏和弦与节拍，sine 通道同场（混合路径回归）。</summary>
    private static ProjectDocument CreatePluginDocument(string clapPath)
    {
        var document = new ProjectDocument { Tempo = 128, SampleRate = DemoProject.SampleRate };
        document.Plugins.Add(new PluginRef { Id = ClapPluginInstance.TestSinePluginId, Path = clapPath });
        document.Channels.Add(new Channel
        {
            Id = 1, Name = "ClapSyn", Kind = ChannelKind.Plugin,
            PluginId = ClapPluginInstance.TestSinePluginId, Volume = 0.6, Pan = -0.25,
        });
        document.Channels.Add(new Channel { Id = 2, Name = "Sine", Kind = ChannelKind.Sine, Volume = 0.3 });
        var pattern = new Pattern { Id = 10, Name = "PluginDemo" };
        foreach (int key in (int[])[60, 64, 67])
        {
            pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = key, Velocity = 0.9, LengthTicks = 192 });
        }
        pattern.Notes.Add(new Note { ChannelId = 1, Tick = 384, Key = 72, Velocity = 0.7, LengthTicks = 96 });
        pattern.Notes.Add(new Note { ChannelId = 2, Tick = 192, Key = 69, Velocity = 0.8, LengthTicks = 96 });
        document.Patterns.Add(pattern);
        // 竖切 4：插件参数自动化——gain 0.05→0.6→0.2 阶梯事件（覆盖静态 0.25）
        List<AutomationPoint> gainLane = document.Channels[0].GetPluginParamPoints(1);
        AutomationLanes.Insert(gainLane, new AutomationPoint { Tick = 0, Value = 0.05 });
        AutomationLanes.Insert(gainLane, new AutomationPoint { Tick = 240, Value = 0.6 });
        AutomationLanes.Insert(gainLane, new AutomationPoint { Tick = 720, Value = 0.2 });
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 480 });
        return document;
    }

    private string CreatePluginPackage(string clapPath)
    {
        string package = Path.Combine(_workDir, "m3-plugin.weft");
        new WeftProjectStore().Save(package, CreatePluginDocument(clapPath),
            new Dictionary<string, string>());
        return package;
    }

    private RenderedProject RenderPluginProject(string package) => new ProjectRenderService(
        new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(),
        new PlanRenderer(), new ClapInstanceFactory()).Render(package);

    [Test]
    public void Plugin_render_is_bitwise_deterministic_in_process()
    {
        string? clapPath = FindTestSineClap();
        if (clapPath is null)
        {
            Assert.Ignore("先构建 native 测试插件");
        }
        string package = CreatePluginPackage(clapPath!);
        RenderedProject first = RenderPluginProject(package);
        RenderedProject second = RenderPluginProject(package);
        Assert.Multiple(() =>
        {
            Assert.That(second.Frames, Is.EqualTo(first.Frames));
            Assert.That(second.Interleaved, Is.EqualTo(first.Interleaved), "插件全链必须逐位确定");
            Assert.That(first.Peak, Is.GreaterThan(0.05), "确有插件输出");
        });
    }

    [Test]
    public void Plugin_render_matches_baseline_within_cross_platform_tolerance()
    {
        string? clapPath = FindTestSineClap();
        if (clapPath is null)
        {
            Assert.Ignore("先构建 native 测试插件");
        }
        string package = CreatePluginPackage(clapPath!);
        RenderedProject rendered = RenderPluginProject(package);

        if (Environment.GetEnvironmentVariable("WEFTONE_GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PluginBaselinePath)!);
            using FileStream stream = File.Create(PluginBaselinePath);
            WavWriter.WriteHeader(stream, rendered.SampleRate, rendered.Channels, rendered.Interleaved.Length);
            WavWriter.WriteSamples(stream, rendered.Interleaved);
            Assert.Warn($"基线已更新: {PluginBaselinePath}（请提交）");
            return;
        }

        Assert.That(File.Exists(PluginBaselinePath), Is.True,
            $"缺少基线 {PluginBaselinePath}；本地设 WEFTONE_GOLDEN_UPDATE=1 生成后提交");
        float[] baseline = ReadFloatWav(PluginBaselinePath);
        Assert.That(baseline.Length, Is.EqualTo(rendered.Interleaved.Length), "样本数与基线不一致");

        double peakDifference = 0;
        for (int i = 0; i < baseline.Length; i++)
        {
            double difference = Math.Abs(baseline[i] - rendered.Interleaved[i]);
            if (difference > peakDifference)
            {
                peakDifference = difference;
            }
        }
        TestContext.Out.WriteLine($"插件黄金对比: 样本 {baseline.Length}，峰值差 {peakDifference:0.0e0}");
        Assert.That(peakDifference, Is.LessThanOrEqualTo(1e-4), "插件黄金音频回归超差");
    }

    /// <summary>M4 拉伸黄金基线：kick 通道 StretchSpeed=0.5（不变调拉长 2×）。</summary>
    private static string StretchBaselinePath
    {
        get
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                dir = dir.Parent!;
            }
            string root = dir?.FullName ?? TestContext.CurrentContext.TestDirectory;
            return Path.Combine(root, "tests", "Weftone.GoldenAudio", "baselines", "m4-stretch-golden.wav");
        }
    }

    private string CreateStretchPackage()
    {
        string kickPath = Path.Combine(_workDir, "kick.wav");
        DemoProject.WriteKickWav(kickPath);
        var document = new ProjectDocument { Tempo = DemoProject.Tempo, SampleRate = DemoProject.SampleRate };
        document.Assets.Add(new AssetRef { Id = "asset-kick", Path = "assets/kick.wav" });
        document.Channels.Add(new Channel
        {
            Id = 1, Name = "SlowKick", Kind = ChannelKind.Sampler,
            AssetId = "asset-kick", StretchSpeed = 0.5, Volume = 0.8,
        });
        var pattern = new Pattern { Id = 10, Name = "StretchDemo" };
        foreach (long tick in (long[])[0, 192, 384])
        {
            pattern.Notes.Add(new Note { ChannelId = 1, Tick = tick, Key = 60, Velocity = 1.0, LengthTicks = 96 });
        }
        document.Patterns.Add(pattern);
        document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
        string package = Path.Combine(_workDir, "m4-stretch.weft");
        new WeftProjectStore().Save(package, document,
            new Dictionary<string, string> { ["assets/kick.wav"] = kickPath });
        return package;
    }

    [Test]
    public void Stretch_render_is_bitwise_deterministic_in_process()
    {
        string package = CreateStretchPackage();
        RenderedProject first = NewService().Render(package);
        RenderedProject second = NewService().Render(package);
        Assert.Multiple(() =>
        {
            Assert.That(second.Interleaved, Is.EqualTo(first.Interleaved), "拉伸路径位级确定");
            Assert.That(first.Peak, Is.GreaterThan(0.1), "拉伸后有能量");
        });
    }

    [Test]
    public void Stretch_render_matches_baseline_within_cross_platform_tolerance()
    {
        string package = CreateStretchPackage();
        RenderedProject rendered = NewService().Render(package);

        if (Environment.GetEnvironmentVariable("WEFTONE_GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StretchBaselinePath)!);
            using FileStream stream = File.Create(StretchBaselinePath);
            WavWriter.WriteHeader(stream, rendered.SampleRate, rendered.Channels, rendered.Interleaved.Length);
            WavWriter.WriteSamples(stream, rendered.Interleaved);
            Assert.Warn($"基线已更新: {StretchBaselinePath}（请提交）");
            return;
        }

        Assert.That(File.Exists(StretchBaselinePath), Is.True,
            $"缺少基线 {StretchBaselinePath}；本地设 WEFTONE_GOLDEN_UPDATE=1 生成后提交");
        float[] baseline = ReadFloatWav(StretchBaselinePath);
        Assert.That(baseline.Length, Is.EqualTo(rendered.Interleaved.Length), "样本数与基线不一致");

        double peakDifference = 0;
        for (int i = 0; i < baseline.Length; i++)
        {
            double difference = Math.Abs(baseline[i] - rendered.Interleaved[i]);
            if (difference > peakDifference)
            {
                peakDifference = difference;
            }
        }
        TestContext.Out.WriteLine($"拉伸黄金对比: 样本 {baseline.Length}，峰值差 {peakDifference:0.0e0}");
        // Signalsmith 为频域算法，跨编译器浮点差可达 1e-2 量级（SPEC M4 风险：容差独立于采样/插件基线）
        Assert.That(peakDifference, Is.LessThanOrEqualTo(2e-2), "拉伸黄金回归超差");
    }

    private static float[] ReadFloatWav(string path)
    {
        byte[] all = File.ReadAllBytes(path);
        Assert.That(all.Length, Is.GreaterThan(44));
        Assert.That(System.Text.Encoding.ASCII.GetString(all, 0, 4), Is.EqualTo("RIFF"));
        Assert.That(System.Text.Encoding.ASCII.GetString(all, 36, 4), Is.EqualTo("data"));
        return MemoryMarshal.Cast<byte, float>(all.AsSpan(44)).ToArray();
    }
}
