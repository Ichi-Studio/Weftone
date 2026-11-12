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
