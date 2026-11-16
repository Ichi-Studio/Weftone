using Weftone.Domain;

namespace Weftone.Domain.Commands;

/// <summary>
/// 添加切片点（SPEC M4 D4/D5）：资产级、样本帧域、升序唯一。
/// 同帧冲突=替换（后者覆盖），撤销精确还原。
/// </summary>
public sealed class AddSlicePointCommand(string assetId, long frame) : ICommand
{
    private readonly string _assetId = assetId;
    private readonly long _frame = frame;
    private long _replacedFrame;
    private bool _hadReplaced;
    private bool _applied;

    public string Title => $"添加切片点 asset={_assetId} frame={_frame}";

    public void Apply(ProjectDocument document)
    {
        List<long> points = FindPoints(document);
        int index = points.BinarySearch(_frame);
        if (index >= 0)
        {
            _replacedFrame = points[index];
            _hadReplaced = true;
            points[index] = _frame;
        }
        else
        {
            points.Insert(~index, _frame);
            _hadReplaced = false;
        }
        _applied = true;
    }

    public void Revert(ProjectDocument document)
    {
        if (!_applied)
        {
            throw new InvalidOperationException("未执行过的命令不可撤销");
        }
        List<long> points = FindPoints(document);
        points.Remove(_frame);
        if (_hadReplaced)
        {
            points.Add(_replacedFrame);
            points.Sort();
        }
    }

    private List<long> FindPoints(ProjectDocument document)
    {
        AssetRef asset = document.Assets.FirstOrDefault(a => a.Id == _assetId)
            ?? throw new InvalidOperationException($"asset {_assetId} 不存在");
        if (_frame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), _frame, "切片帧不可为负");
        }
        return asset.SlicePoints;
    }
}

/// <summary>删除切片点（SPEC M4）：按帧定位，撤销精确还原。</summary>
public sealed class RemoveSlicePointCommand(string assetId, long frame) : ICommand
{
    private readonly string _assetId = assetId;
    private readonly long _frame = frame;
    private bool _applied;

    public string Title => $"删除切片点 asset={_assetId} frame={_frame}";

    public void Apply(ProjectDocument document)
    {
        List<long> points = FindPoints(document);
        int index = points.BinarySearch(_frame);
        if (index < 0)
        {
            throw new InvalidOperationException($"asset {_assetId} 无切片点 frame={_frame}");
        }
        points.RemoveAt(index);
        _applied = true;
    }

    public void Revert(ProjectDocument document)
    {
        if (!_applied)
        {
            throw new InvalidOperationException("未执行过的命令不可撤销");
        }
        List<long> points = FindPoints(document);
        int index = points.BinarySearch(_frame);
        points.Insert(index < 0 ? ~index : index, _frame);
    }

    private List<long> FindPoints(ProjectDocument document)
    {
        AssetRef asset = document.Assets.FirstOrDefault(a => a.Id == _assetId)
            ?? throw new InvalidOperationException($"asset {_assetId} 不存在");
        return asset.SlicePoints;
    }
}

/// <summary>移动切片点（SPEC M4：拖拽编辑，from/to 显式，事务合并为单步撤销）。</summary>
public sealed class MoveSlicePointCommand(string assetId, long fromFrame, long toFrame) : ICommand
{
    private readonly string _assetId = assetId;
    private readonly long _fromFrame = fromFrame;
    private readonly long _toFrame = toFrame;

    public string Title => $"移动切片点 asset={_assetId} { _fromFrame}→{_toFrame}";

    public void Apply(ProjectDocument document)
    {
        List<long> points = FindPoints(document);
        int index = points.BinarySearch(_fromFrame);
        if (index < 0)
        {
            throw new InvalidOperationException($"asset {_assetId} 无切片点 frame={_fromFrame}");
        }
        points[index] = _toFrame;
        points.Sort();
    }

    public void Revert(ProjectDocument document)
    {
        List<long> points = FindPoints(document);
        int index = points.BinarySearch(_toFrame);
        if (index < 0)
        {
            throw new InvalidOperationException($"asset {_assetId} 无切片点 frame={_toFrame}");
        }
        points[index] = _fromFrame;
        points.Sort();
    }

    private List<long> FindPoints(ProjectDocument document)
    {
        AssetRef asset = document.Assets.FirstOrDefault(a => a.Id == _assetId)
            ?? throw new InvalidOperationException($"asset {_assetId} 不存在");
        if (_toFrame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toFrame), _toFrame, "切片帧不可为负");
        }
        return asset.SlicePoints;
    }
}
