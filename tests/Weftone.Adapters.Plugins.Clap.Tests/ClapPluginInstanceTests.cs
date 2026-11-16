using NUnit.Framework;
using Weftone.Abstractions.Plugins;
using Weftone.Adapters.Plugins.Clap;

namespace Weftone.Adapters.Plugins.Clap.Tests;

/// <summary>SPEC M3 竖切 2：CLAP 实例出声——确定性 sine、事件时序、复音、生命周期。
/// 依赖 native 构建产物，缺失时跳过。</summary>
public class ClapPluginInstanceTests
{
    private const int SampleRate = 48_000;
    private string? _sineClap;

    [SetUp]
    public void SetUp()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent!;
        }
        string? root = dir?.FullName;
        _sineClap = root is null
            ? null
            : Directory.EnumerateFiles(Path.Combine(root, "native", "build"),
                "weftone_test_sine.clap", SearchOption.AllDirectories).FirstOrDefault();
        if (_sineClap is null)
        {
            Assert.Ignore("先构建 native 测试插件（cmake -S native -B native/build && cmake --build）");
        }
    }

    private static PluginNoteEvent On(int frame, int key, float velocity = 1f) =>
        new(PluginNoteEventKind.On, frame, key, 0, velocity);

    private static PluginNoteEvent Off(int frame, int key) =>
        new(PluginNoteEventKind.Off, frame, key, 0, 0f);

    private static double Sine(int sample, int key)
        => Math.Sin(2.0 * Math.PI * 440.0 * Math.Pow(2.0, (key - 69) / 12.0) * sample / SampleRate) * 0.25;

    [Test]
    public void Note_on_renders_deterministic_sine_on_both_channels()
    {
        using var plugin = ClapPluginInstance.Create(_sineClap!, ClapPluginInstance.TestSinePluginId, SampleRate);
        var left = new float[256];
        var right = new float[256];
        plugin.Process(left, right, [On(0, 69)]);

        Assert.Multiple(() =>
        {
            Assert.That(left[0], Is.EqualTo(0f).Within(1e-6f), "起点相位 0");
            Assert.That(left[16], Is.EqualTo(Sine(16, 69)).Within(1e-4f), "440Hz 解析对比");
            Assert.That(right[16], Is.EqualTo(left[16]).Within(1e-7f), "L==R");
            Assert.That(Math.Abs(left[128]), Is.GreaterThan(0.1f), "有声");
        });
    }

    [Test]
    public void Phase_continues_across_process_blocks()
    {
        using var plugin = ClapPluginInstance.Create(_sineClap!, ClapPluginInstance.TestSinePluginId, SampleRate);
        var left = new float[512];
        var right = new float[512];
        plugin.Process(left.AsSpan(0, 256), right.AsSpan(0, 256), [On(0, 69)]);
        plugin.Process(left.AsSpan(256, 256), right.AsSpan(256, 256), []);

        Assert.That(left[300], Is.EqualTo(Sine(300, 69)).Within(1e-4f), "第二块延续相位");
    }

    [Test]
    public void Note_off_stops_voice_immediately_at_event_frame()
    {
        using var plugin = ClapPluginInstance.Create(_sineClap!, ClapPluginInstance.TestSinePluginId, SampleRate);
        var left = new float[256];
        var right = new float[256];
        plugin.Process(left, right, [On(0, 69), Off(128, 69)]);

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(left[100]), Is.GreaterThan(0.1f), "off 前有声");
            Assert.That(left[128], Is.EqualTo(0f).Within(1e-6f), "off 帧立即静音");
            Assert.That(left[255], Is.EqualTo(0f).Within(1e-6f));
        });
    }

    [Test]
    public void Polyphony_sums_two_notes()
    {
        using var plugin = ClapPluginInstance.Create(_sineClap!, ClapPluginInstance.TestSinePluginId, SampleRate);
        var left = new float[256];
        var right = new float[256];
        plugin.Process(left, right, [On(0, 60), On(0, 67)]);

        double expected = Sine(16, 60) + Sine(16, 67);
        Assert.That(left[16], Is.EqualTo(expected).Within(1e-3f), "复音叠加");
    }

    [Test]
    public void Retrigger_same_key_resets_phase_and_unknown_plugin_fails()
    {
        using var plugin = ClapPluginInstance.Create(_sineClap!, ClapPluginInstance.TestSinePluginId, SampleRate);
        var left = new float[512];
        var right = new float[512];
        plugin.Process(left.AsSpan(0, 256), right.AsSpan(0, 256), [On(0, 69)]);
        plugin.Process(left.AsSpan(256, 256), right.AsSpan(256, 256), [On(0, 69)]);
        Assert.That(left[256], Is.EqualTo(Sine(0, 69)).Within(1e-4f), "同 key 重触发相位归零");

        Assert.That(() => ClapPluginInstance.Create(_sineClap!, "com.unknown.nope", SampleRate),
            Throws.InvalidOperationException, "未知 plugin id 拒绝创建");
    }
}
