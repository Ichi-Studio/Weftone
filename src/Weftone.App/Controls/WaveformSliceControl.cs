using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Weftone.App.ViewModels;
using Weftone.Domain;

namespace Weftone.App.Controls;

/// <summary>
/// 波形切片编辑控件（SPEC M4 竖切 3）：峰值柱状波形 + 切片点竖线（编号=key 偏移），
/// 手势——拖切片点移动（事务合并单步撤销）、空白处左键加切片、右键删切片。
/// </summary>
public sealed class WaveformSliceControl : Control
{
    public static readonly StyledProperty<MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<WaveformSliceControl, MainWindowViewModel?>(nameof(ViewModel));

    public static readonly StyledProperty<string?> AssetIdProperty =
        AvaloniaProperty.Register<WaveformSliceControl, string?>(nameof(AssetId));

    public static readonly StyledProperty<long> TotalFramesProperty =
        AvaloniaProperty.Register<WaveformSliceControl, long>(nameof(TotalFrames));

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x22));
    private static readonly IBrush WaveBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x9A, 0xC8));
    private static readonly IBrush SliceBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x66));
    private static readonly IPen SlicePen = new Pen(SliceBrush, 1.5);

    private float[] _peaks = [];
    private bool _dragging;
    private long _dragFromFrame;
    private long _dragCurrentFrame;

    public MainWindowViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public string? AssetId
    {
        get => GetValue(AssetIdProperty);
        set => SetValue(AssetIdProperty, value);
    }

    public long TotalFrames
    {
        get => GetValue(TotalFramesProperty);
        set => SetValue(TotalFramesProperty, value);
    }

    static WaveformSliceControl()
    {
        AssetIdProperty.Changed.AddClassHandler<WaveformSliceControl>(static (c, _) => c.ReloadPeaks());
        TotalFramesProperty.Changed.AddClassHandler<WaveformSliceControl>(static (c, _) => c.InvalidateVisual());
    }

    /// <summary>当前资产（优先 VM 选中通道，其次显式 AssetId）。</summary>
    private string? EffectiveAssetId =>
        ViewModel?.SelectedSliceChannel?.AssetId ?? AssetId;

    /// <summary>当前资产帧数（横轴映射）。</summary>
    private long EffectiveFrames =>
        ViewModel?.SelectedSliceChannel is { } channel && channel.AssetId is not null
            ? ViewModel.GetAssetFrameCount(channel.AssetId)
            : TotalFrames;

    /// <summary>重新取峰值（资产切换时）。</summary>
    public void ReloadPeaks()
    {
        MainWindowViewModel? vm = ViewModel;
        string? assetId = EffectiveAssetId;
        _peaks = vm is not null && assetId is not null ? vm.GetAssetPeaks(assetId) : [];
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));
        double width = Bounds.Width;
        double mid = Bounds.Height / 2;
        if (_peaks.Length > 0 && width > 0)
        {
            double barWidth = Math.Max(1, width / _peaks.Length);
            for (int i = 0; i < _peaks.Length; i++)
            {
                float peak = Math.Clamp(_peaks[i], 0f, 1f);
                double h = peak * (Bounds.Height / 2 - 4);
                double x = i * barWidth;
                context.FillRectangle(WaveBrush, new Rect(x, mid - h, Math.Max(1, barWidth - 0.5), h * 2));
            }
        }
        MainWindowViewModel? vm = ViewModel;
        string? assetId = EffectiveAssetId;
        if (vm is null || assetId is null)
        {
            return;
        }
        IReadOnlyList<long> slices = vm.GetSlicePoints(assetId);
        long frames = EffectiveFrames;
        for (int i = 0; i < slices.Count; i++)
        {
            double x = WaveformGeometry.FrameToX(slices[i], frames, width);
            context.DrawLine(SlicePen, new Point(x, 0), new Point(x, Bounds.Height));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        MainWindowViewModel? vm = ViewModel;
        string? assetId = EffectiveAssetId;
        if (vm is null || assetId is null)
        {
            return;
        }
        Point point = e.GetPosition(this);
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        long frames = EffectiveFrames;
        IReadOnlyList<long> slices = vm.GetSlicePoints(assetId);
        long? hit = WaveformGeometry.HitTestSlice(slices, point.X, frames, Bounds.Width);

        if (props.IsRightButtonPressed)
        {
            if (hit is not null)
            {
                vm.RemoveSlicePoint(assetId, hit.Value);
                ReloadPeaks();
                InvalidateVisual();
                e.Handled = true;
            }
            return;
        }
        if (!props.IsLeftButtonPressed)
        {
            return;
        }
        if (hit is not null)
        {
            vm.BeginSlicePointDrag();
            _dragging = true;
            _dragFromFrame = hit.Value;
            _dragCurrentFrame = hit.Value;
        }
        else
        {
            long frame = WaveformGeometry.XToFrame(point.X, frames, Bounds.Width);
            vm.AddSlicePoint(assetId, frame);
            ReloadPeaks();
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || ViewModel is null || EffectiveAssetId is not string assetId)
        {
            return;
        }
        Point point = e.GetPosition(this);
        long frame = WaveformGeometry.XToFrame(point.X, EffectiveFrames, Bounds.Width);
        if (frame == _dragCurrentFrame)
        {
            return;
        }
        ViewModel.DragSlicePoint(assetId, _dragCurrentFrame, frame);
        _dragCurrentFrame = frame;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            ViewModel?.EndSlicePointDrag();
            InvalidateVisual();
        }
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}
