namespace Weftone.Domain;

/// <summary>工程存取端口（ADR-0004/0006）。实现在 adapter，核心不接触 zip/JSON 细节。</summary>
public interface IProjectStore
{
    /// <summary>打开 .weft：校验 schema 版本、SHA256、防 zip slip。</summary>
    LoadedProject Load(string path);

    /// <summary>保存 .weft。assetSourceFiles：相对路径 → 源文件；
    /// pluginStateFiles（SPEC M3 竖切 3）：channelId → state chunk 源文件（写 presets/）。</summary>
    void Save(string path, ProjectDocument document, IReadOnlyDictionary<string, string> assetSourceFiles,
        IReadOnlyDictionary<int, string>? pluginStateFiles = null);
}

/// <summary>加载结果：文档 + 按需读取资产字节流的委托（不解码，解码经 ISampleDecoder）；
/// OpenEntry：按 zip 内相对路径读任意白名单条目（插件 state 等）。</summary>
public sealed record LoadedProject(
    ProjectDocument Document,
    Func<AssetRef, Stream> OpenAsset,
    Func<string, Stream>? OpenEntry = null);
