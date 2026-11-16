using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Weftone.Abstractions.Plugins;

namespace Weftone.Adapters.Plugins.Clap;

/// <summary>
/// CLAP 扫描器（SPEC M3 D1=a）：每个 .clap 在独立 weftone-plugin-scan 进程中加载——
/// 崩溃/挂死只丢该文件的扫描（AGENTS.md §11）；%LocalAppData%\Weftone\plugins\cache.json
/// 按 (路径, 大小, mtime) 缓存，未变化不重扫。
/// </summary>
public sealed class ClapScanner : IPluginScanner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _scannerExecutable;
    private readonly string _cachePath;
    private readonly List<string> _recentFailures = [];

    /// <summary>最近一次 ScanAsync 的子进程失败诊断（stderr 尾行；测试/排查用）。</summary>
    public IReadOnlyList<string> RecentFailures => _recentFailures;

    public ClapScanner(string? scannerExecutablePath = null, string? cachePath = null)
    {
        _scannerExecutable = scannerExecutablePath ?? FindScannerExecutable()
            ?? throw new InvalidOperationException(
                "找不到 weftone-plugin-scan（构建 tools/Weftone.PluginScan 或显式传入路径）");
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Weftone", "plugins", "cache.json");
    }

    public async Task<PluginScanResult> ScanAsync(
        IReadOnlyList<string> directories,
        TimeSpan? perFileTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);
        TimeSpan timeout = perFileTimeout ?? DefaultTimeout;
        Dictionary<string, CacheEntry> cache = LoadCache();
        var plugins = new List<ScannedPlugin>();
        var failed = new List<string>();
        _recentFailures.Clear();

        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(directory, "*.clap"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string full = Path.GetFullPath(file);
                if (TryFromCache(cache, full, out ScannedPlugin[]? cached))
                {
                    plugins.AddRange(cached);
                    continue;
                }
                CacheEntry? entry = await ScanFileAsync(full, timeout, cancellationToken);
                if (entry is null)
                {
                    _recentFailures.Add($"{full}: {_lastScanError ?? "未知"}");
                    failed.Add(full);
                    continue; // 失败不入缓存：下次扫描自动重试（重建后 mtime 已变）
                }
                entry.Stamp(full);
                cache[full] = entry;
                foreach (PluginDescriptorJson json in entry.Plugins)
                {
                    plugins.Add(new ScannedPlugin(full, json.ToDescriptor()));
                }
            }
        }
        SaveCache(cache);
        return new PluginScanResult(plugins, failed);
    }

    private string? _lastScanError;

    private async Task<CacheEntry?> ScanFileAsync(string file, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _scannerExecutable,
                ArgumentList = { file },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
            },
        };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _lastScanError = $"启动失败: {ex.Message}";
            return null;
        }
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            string error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            if (process.ExitCode != 0)
            {
                // 诊断优先 stderr 尾行（CLI/崩溃信息），否则退出码
                string tail = error.Trim();
                if (tail.Length > 0)
                {
                    tail = tail.Split('
')[^1].Trim();
                }
                _lastScanError = $"exit={process.ExitCode} {tail}";
                return null;
            }
            return new CacheEntry { Plugins = ParseOutput(output) };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 超时（或插件挂死）：杀扫描进程，只丢该文件
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            _lastScanError = "超时";
            return null;
        }
        catch (JsonException ex)
        {
            _lastScanError = $"JSON: {ex.Message}";
            return null;
        }
    }

    private static PluginDescriptorJson[] ParseOutput(string output)
    {
        var descriptors = new List<PluginDescriptorJson>();
        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            descriptors.Add(JsonSerializer.Deserialize<PluginDescriptorJson>(line, JsonOptions)
                ?? throw new JsonException("空描述行"));
        }
        return [.. descriptors];
    }

    private static bool TryFromCache(Dictionary<string, CacheEntry> cache, string file,
        [NotNullWhen(true)] out ScannedPlugin[]? cached)
    {
        cached = null;
        if (!cache.TryGetValue(file, out CacheEntry? entry)
            || entry.Plugins is not { Length: > 0 })
        {
            return false;
        }
        var info = new FileInfo(file);
        if (!info.Exists || info.Length != entry.Size || info.LastWriteTimeUtc != entry.MtimeUtc)
        {
            return false;
        }
        cached = [.. entry.Plugins.Select(p => new ScannedPlugin(file, p.ToDescriptor()))];
        return true;
    }

    private Dictionary<string, CacheEntry> LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return [];
            }
            using FileStream stream = File.OpenRead(_cachePath);
            return JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(stream, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void SaveCache(Dictionary<string, CacheEntry> cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            string tmpPath = _cachePath + ".tmp";
            using (FileStream stream = File.Create(tmpPath))
            {
                JsonSerializer.Serialize(stream, cache, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmpPath, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 缓存不可写不影响扫描结果
        }
    }

    private static string? FindScannerExecutable()
    {
        string exe = OperatingSystem.IsWindows()
            ? "weftone-plugin-scan.exe"
            : "weftone-plugin-scan";
        string direct = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(direct))
        {
            return direct;
        }
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string toolsBin = Path.Combine(dir.FullName, "tools", "Weftone.PluginScan", "bin");
            if (!Directory.Exists(toolsBin))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(toolsBin, exe, SearchOption.AllDirectories))
            {
                return file;
            }
        }
        return null;
    }

    internal sealed class CacheEntry
    {
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("mtimeUtc")] public DateTime MtimeUtc { get; set; }
        [JsonPropertyName("plugins")] public PluginDescriptorJson[] Plugins { get; set; } = [];

        /// <summary>成功条目回填文件指纹（缓存失效判定）。</summary>
        public void Stamp(string file)
        {
            var info = new FileInfo(file);
            if (info.Exists)
            {
                Size = info.Length;
                MtimeUtc = info.LastWriteTimeUtc;
            }
        }
    }

    internal sealed class PluginDescriptorJson
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("vendor")] public string Vendor { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("features")] public string[] Features { get; set; } = [];

        public PluginDescriptor ToDescriptor() => new(Id, Name, Vendor, Version, Description, Features);
    }
}
