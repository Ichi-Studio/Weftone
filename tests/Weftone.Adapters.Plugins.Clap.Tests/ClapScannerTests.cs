using NUnit.Framework;
using Weftone.Abstractions.Plugins;
using Weftone.Adapters.Plugins.Clap;

namespace Weftone.Adapters.Plugins.Clap.Tests;

/// <summary>SPEC M3 竖切 1：扫描隔离——崩溃插件只丢扫描进程、坏文件不阻断、缓存命中与失效。
/// 依赖 native 构建产物（weftone-plugin-scan CLI 与 native/build/*.clap），缺失时跳过。</summary>
public class ClapScannerTests
{
    private string _workDir = "";
    private string _cachePath = "";
    private string _scanDir = "";
    private string? _cliPath;
    private string? _sineClap;
    private string? _crashClap;

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "weftone-clapscan-" + Guid.NewGuid().ToString("N"));
        _cachePath = Path.Combine(_workDir, "cache.json");
        _scanDir = Path.Combine(_workDir, "plugins");
        Directory.CreateDirectory(_scanDir);
        _cliPath = FindRepoArtifact(
            Path.Combine("tools", "Weftone.PluginScan", "bin"),
            OperatingSystem.IsWindows() ? "weftone-plugin-scan.exe" : "weftone-plugin-scan");
        _sineClap = FindRepoArtifact(Path.Combine("native", "build"), "weftone_test_sine.clap");
        _crashClap = FindRepoArtifact(Path.Combine("native", "build"), "weftone_crash.clap");
        if (_cliPath is null || _sineClap is null || _crashClap is null)
        {
            Assert.Ignore("先构建 native 与 weftone-plugin-scan（cmake -S native -B native/build && dotnet build）");
        }
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_workDir, recursive: true);

    private static string? FindRepoArtifact(string relativeDir, string fileName)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent!;
        }
        if (dir is null)
        {
            return null;
        }
        string root = Path.Combine(dir.FullName, relativeDir);
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    private ClapScanner NewScanner(string? cliPath = null) => new(cliPath ?? _cliPath, _cachePath);

    private string Diagnostics(ClapScanner scanner)
        => scanner.RecentFailures.Count == 0 ? "" : string.Join(" | ", scanner.RecentFailures.Take(3));

    [Test]
    public async Task Scans_test_sine_descriptor_via_isolated_process()
    {
        File.Copy(_sineClap!, Path.Combine(_scanDir, "sine.clap"));
        ClapScanner scanner = NewScanner();
        PluginScanResult result = await scanner.ScanAsync([_scanDir]);

        Assert.That(result.FailedFiles, Is.Empty, $"扫描失败: {Diagnostics(scanner)}");
        ScannedPlugin plugin = result.Plugins.Single();
        Assert.Multiple(() =>
        {
            Assert.That(plugin.Descriptor.Id, Is.EqualTo("io.github.ichi-studio.weftone.test-sine"));
            Assert.That(plugin.Descriptor.Name, Is.EqualTo("Weftone Test Sine"));
            Assert.That(plugin.Descriptor.Vendor, Is.EqualTo("Ichi Studio"));
            Assert.That(plugin.Descriptor.Version, Is.EqualTo("1.0.0"));
            Assert.That(plugin.Descriptor.Features, Is.EqualTo(new[] { "instrument", "synthesizer" }));
            Assert.That(plugin.FilePath, Does.EndWith("sine.clap"));
        });
    }

    /// <summary>AGENTS.md §11 硬约束：abort() 插件只带走扫描进程，宿主继续收其余插件。</summary>
    [Test]
    public async Task Crashing_plugin_kills_only_scan_process_and_rest_survive()
    {
        File.Copy(_sineClap!, Path.Combine(_scanDir, "a_sine.clap"));
        File.Copy(_crashClap!, Path.Combine(_scanDir, "b_crash.clap"));
        await File.WriteAllTextAsync(Path.Combine(_scanDir, "c_not_a_clap.clap"), "garbage");

        ClapScanner scanner = NewScanner();
        PluginScanResult result = await scanner.ScanAsync([_scanDir]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Plugins, Has.Count.EqualTo(1), $"崩溃与坏文件之外的插件照常收得: {Diagnostics(scanner)}");
            Assert.That(result.Plugins[0].Descriptor.Id, Is.EqualTo("io.github.ichi-studio.weftone.test-sine"));
            Assert.That(result.FailedFiles.Select(Path.GetFileName),
                Is.EqualTo(new[] { "b_crash.clap", "c_not_a_clap.clap" }), "崩溃与坏文件进失败清单");
        });
    }

    [Test]
    public async Task Cache_hit_skips_process_and_mtime_change_invalidates()
    {
        string target = Path.Combine(_scanDir, "sine.clap");
        File.Copy(_sineClap!, target);

        PluginScanResult first = await NewScanner().ScanAsync([_scanDir]);
        Assert.That(first.Plugins, Has.Count.EqualTo(1));
        Assert.That(File.Exists(_cachePath), Is.True, "扫描后写入缓存");

        // 缓存命中：CLI 指向不存在的路径也能出结果（不再起进程）
        PluginScanResult cached = await NewScanner(cliPath: Path.Combine(_workDir, "missing-cli")).ScanAsync([_scanDir]);
        Assert.That(cached.Plugins, Has.Count.EqualTo(1), "缓存命中不出进程");

        // mtime 变化 → 缓存失效；坏 CLI 起不了进程 → 进失败清单
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddMinutes(1));
        PluginScanResult invalidated = await NewScanner(cliPath: Path.Combine(_workDir, "missing-cli")).ScanAsync([_scanDir]);
        Assert.Multiple(() =>
        {
            Assert.That(invalidated.Plugins, Is.Empty, "失效后必须重扫");
            Assert.That(invalidated.FailedFiles, Is.EqualTo(new[] { target }), "重扫失败（CLI 不可用）");
        });
    }

    [Test]
    public async Task Missing_directory_yields_empty_result()
    {
        PluginScanResult result = await NewScanner().ScanAsync([Path.Combine(_workDir, "nope")]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Plugins, Is.Empty);
            Assert.That(result.FailedFiles, Is.Empty);
        });
    }
}
