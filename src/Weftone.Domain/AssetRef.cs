namespace Weftone.Domain;

/// <summary>资产引用：工程包内相对路径（assets/ 下）+ 内容 SHA256（小写 hex）；
/// 切片点（SPEC M4 D4=a，样本帧域，升序唯一）——beat-slice 演奏的切片标记。</summary>
public sealed class AssetRef
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    /// <summary>切片点（样本帧位置，升序；第 i 个切片由 key=60+i 触发）。</summary>
    public List<long> SlicePoints { get; } = [];
}
