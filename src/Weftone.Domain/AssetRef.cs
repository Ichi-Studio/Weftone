namespace Weftone.Domain;

/// <summary>资产引用：工程包内相对路径（assets/ 下）+ 内容 SHA256（小写 hex）。</summary>
public sealed class AssetRef
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
