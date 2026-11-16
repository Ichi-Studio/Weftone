using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Weftone.Adapters.Plugins.Clap;
using Weftone.Abstractions.Plugins;

namespace Weftone.Tools.PluginScan;

/// <summary>
/// 独立插件扫描进程（SPEC M3 D1=a / AGENTS.md §11）：
/// 宿主为每个 .clap 文件启动本进程——插件 init/加载崩溃只丢本进程。
/// 输出：每行一个 JSON 描述到 stdout；退出码 0=扫描完成（含 0 插件），2=加载失败。
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 描述含任意语言名，自家人读
    };

    public static int Main(string[] args)
    {
        // Windows 中文 locale 下 Console 默认 GBK——描述含中文时父进程按 UTF-8 解析会错位
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length != 1)
        {
            Console.Error.WriteLine("用法: weftone-plugin-scan <file.clap>");
            return 2;
        }
        if (!File.Exists(args[0]))
        {
            Console.Error.WriteLine($"文件不存在: {args[0]}");
            return 2;
        }
        if (!ClapBridgeNative.TryScan(args[0], out PluginDescriptor[] descriptors))
        {
            Console.Error.WriteLine($"scan failed: {args[0]}");
            Console.Error.WriteLine($"probe dir: {AppContext.BaseDirectory}");
            return 2;
        }
        foreach (PluginDescriptor descriptor in descriptors)
        {
            Console.WriteLine(JsonSerializer.Serialize(new DescriptorJson
            {
                Id = descriptor.Id,
                Name = descriptor.Name,
                Vendor = descriptor.Vendor,
                Version = descriptor.Version,
                Description = descriptor.Description,
                Features = [.. descriptor.Features],
            }, JsonOptions));
        }
        return 0;
    }

    private sealed class DescriptorJson
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("vendor")] public string Vendor { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("features")] public string[] Features { get; set; } = [];
    }
}
