using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weftone.Domain;

namespace Weftone.Adapters.ProjectStore;

/// <summary>
/// .weft v1 存取（ADR-0006）：zip 容器 = metadata.json + project.json + assets/*。
/// 加载逐条校验：条目白名单（防 zip slip）、schema 版本、域规则、资产 SHA256。
/// </summary>
public sealed class WeftProjectStore : IProjectStore
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public LoadedProject Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            ValidateEntryName(entry.FullName);
        }

        ZipArchiveEntry? projectEntry = zip.GetEntry("project.json");
        if (projectEntry is null)
        {
            throw new InvalidProjectException("缺少 project.json");
        }
        ZipArchiveEntry? metadataEntry = zip.GetEntry("metadata.json");
        if (metadataEntry is null)
        {
            throw new InvalidProjectException("缺少 metadata.json");
        }

        var metadata = Deserialize<MetadataJson>(metadataEntry);
        if (metadata.SchemaVersion != SchemaVersion)
        {
            throw new InvalidProjectException($"schema 版本不支持: {metadata.SchemaVersion}（当前 {SchemaVersion}）");
        }

        var json = Deserialize<ProjectJson>(projectEntry);
        if (json.SchemaVersion != SchemaVersion)
        {
            throw new InvalidProjectException($"project.json schema 版本不支持: {json.SchemaVersion}");
        }
        ProjectDocument document = SchemaMapper.ToDomain(json);

        foreach (AssetRef asset in document.Assets)
        {
            ZipArchiveEntry? assetEntry = zip.GetEntry(asset.Path);
            if (assetEntry is null)
            {
                throw new InvalidProjectException($"资产缺失: {asset.Path}");
            }
            using Stream stream = assetEntry.Open();
            string actual = ComputeSha256(stream);
            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidProjectException($"资产 SHA256 不匹配: {asset.Path} 期望 {asset.Sha256} 实际 {actual}");
            }
        }

        foreach (Channel channel in document.Channels)
        {
            if (string.IsNullOrEmpty(channel.PluginStateEntry))
            {
                continue;
            }
            ZipArchiveEntry? stateEntry = zip.GetEntry(channel.PluginStateEntry);
            if (stateEntry is null)
            {
                throw new InvalidProjectException($"插件状态缺失: {channel.PluginStateEntry}");
            }
            using Stream stream = stateEntry.Open();
            string actual = ComputeSha256(stream);
            if (!string.Equals(actual, channel.PluginStateSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidProjectException(
                    $"插件状态 SHA256 不匹配: {channel.PluginStateEntry} 期望 {channel.PluginStateSha256} 实际 {actual}");
            }
        }

        return new LoadedProject(document, asset => OpenAssetStream(path, asset.Path),
            entryPath => OpenAssetStream(path, entryPath));
    }

    public void Save(string path, ProjectDocument document, IReadOnlyDictionary<string, string> assetSourceFiles,
        IReadOnlyDictionary<int, string>? pluginStateFiles = null)
    {
        string fullPath = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var assets = new List<(AssetRef Reference, string SourcePath)>();
        foreach (KeyValuePair<string, string> pair in assetSourceFiles)
        {
            RequireLocalEntryName(pair.Key);
            AssetRef? reference = document.Assets.FirstOrDefault(a => a.Path == pair.Key)
                ?? throw new InvalidProjectException($"资产源未在文档注册: {pair.Key}");
            using Stream source = File.OpenRead(pair.Value);
            reference.Sha256 = ComputeSha256(source);
            assets.Add((reference, pair.Value));
        }

        var json = SchemaMapper.ToJson(document);

        using var zip = new ZipArchive(File.Create(fullPath), ZipArchiveMode.Create);
        WriteEntry(zip, "metadata.json", JsonSerializer.Serialize(new MetadataJson
        {
            SchemaVersion = SchemaVersion,
            Generator = "weftone",
        }, JsonOptions));
        WriteEntry(zip, "project.json", JsonSerializer.Serialize(json, JsonOptions));

        foreach ((AssetRef reference, string sourcePath) in assets)
        {
            ZipArchiveEntry entry = zip.CreateEntry(reference.Path);
            using Stream source = File.OpenRead(sourcePath);
            using Stream target = entry.Open();
            source.CopyTo(target);
        }

        if (pluginStateFiles is not null)
        {
            foreach (KeyValuePair<int, string> pair in pluginStateFiles)
            {
                Channel? channel = document.Channels.FirstOrDefault(c => c.Id == pair.Key);
                if (channel is null)
                {
                    throw new InvalidProjectException($"插件状态源对应未知 channel: {pair.Key}");
                }
                string entryPath = $"presets/state-{pair.Key}.bin";
                using (Stream source = File.OpenRead(pair.Value))
                {
                    channel.PluginStateSha256 = ComputeSha256(source);
                    source.Position = 0;
                    ZipArchiveEntry stateEntry = zip.CreateEntry(entryPath);
                    using Stream target = stateEntry.Open();
                    source.CopyTo(target);
                }
                channel.PluginStateEntry = entryPath;
            }
        }
    }

    private static Stream OpenAssetStream(string packagePath, string assetPath)
    {
        var zip = ZipFile.OpenRead(packagePath);
        try
        {
            ZipArchiveEntry? entry = zip.GetEntry(assetPath);
            if (entry is null)
            {
                throw new InvalidProjectException($"资产缺失: {assetPath}");
            }
            return new EntryStream(entry.Open(), zip);
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || Path.IsPathRooted(name)
            || name.Split('/').Contains(".."))
        {
            throw new InvalidProjectException($"非法 zip 条目: {name}");
        }
        if (name != "project.json" && name != "metadata.json"
            && !name.StartsWith("assets/", StringComparison.Ordinal)
            && !name.StartsWith("presets/", StringComparison.Ordinal))
        {
            throw new InvalidProjectException($"非白名单 zip 条目: {name}");
        }
    }

    private static void RequireLocalEntryName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || Path.IsPathRooted(name)
            || name.Split('/').Contains("..") || !name.StartsWith("assets/", StringComparison.Ordinal))
        {
            throw new InvalidProjectException($"非法资产路径: {name}");
        }
    }

    private static T Deserialize<T>(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        try
        {
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new InvalidProjectException($"{entry.FullName} 解析为空");
        }
        catch (JsonException ex)
        {
            throw new InvalidProjectException($"{entry.FullName} JSON 解析失败: {ex.Message}", ex);
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ComputeSha256(Stream stream)
    {
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>随流处置 zip 的包装（OpenAsset 跨 using 作用域）。</summary>
    private sealed class EntryStream(Stream inner, ZipArchive owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
