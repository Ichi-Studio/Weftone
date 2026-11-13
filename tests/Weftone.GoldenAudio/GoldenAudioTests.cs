using System.Runtime.InteropServices;
using NUnit.Framework;
using Weftone.Adapters.Codecs;
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
        new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());

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

    private static float[] ReadFloatWav(string path)
    {
        byte[] all = File.ReadAllBytes(path);
        Assert.That(all.Length, Is.GreaterThan(44));
        Assert.That(System.Text.Encoding.ASCII.GetString(all, 0, 4), Is.EqualTo("RIFF"));
        Assert.That(System.Text.Encoding.ASCII.GetString(all, 36, 4), Is.EqualTo("data"));
        return MemoryMarshal.Cast<byte, float>(all.AsSpan(44)).ToArray();
    }
}
