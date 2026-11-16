namespace Weftone.Abstractions.Plugins;

/// <summary>插件元数据（扫描产物，格式无关；M3 = CLAP）。</summary>
public sealed record PluginDescriptor(
    string Id,
    string Name,
    string Vendor,
    string Version,
    string Description,
    IReadOnlyList<string> Features);

/// <summary>扫描到的插件：来源文件 + 描述。</summary>
public sealed record ScannedPlugin(string FilePath, PluginDescriptor Descriptor);

/// <summary>一次目录扫描结果：成功清单 + 失败文件（崩溃/超时/非 CLAP，不阻断整体）。</summary>
public sealed record PluginScanResult(IReadOnlyList<ScannedPlugin> Plugins, IReadOnlyList<string> FailedFiles);

/// <summary>
/// 插件扫描端口（ADR-0004/0007）：实现必须在独立进程加载插件
/// （AGENTS.md §11：插件崩溃只丢扫描进程），并维护本地缓存避免重复扫描。
/// </summary>
public interface IPluginScanner
{
    Task<PluginScanResult> ScanAsync(
        IReadOnlyList<string> directories,
        TimeSpan? perFileTimeout = null,
        CancellationToken cancellationToken = default);
}
