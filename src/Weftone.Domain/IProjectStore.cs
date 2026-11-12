namespace Weftone.Domain;

/// <summary>工程存取端口（ADR-0004/0006）。实现在 adapter，核心不接触 zip/JSON 细节。</summary>
public interface IProjectStore
{
    /// <summary>打开 .weft：校验 schema 版本、SHA256、防 zip slip。</summary>
    LoadedProject Load(string path);

    /// <summary>保存 .weft。assetSourceFiles：相对路径 → 源文件。</summary>
    void Save(string path, ProjectDocument document, IReadOnlyDictionary<string, string> assetSourceFiles);
}

/// <summary>加载结果：文档 + 按需读取资产字节流的委托（不解码，解码经 ISampleDecoder）。</summary>
public sealed record LoadedProject(ProjectDocument Document, Func<AssetRef, Stream> OpenAsset);
