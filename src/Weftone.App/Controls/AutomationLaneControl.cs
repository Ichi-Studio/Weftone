using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Weftone.App.ViewModels;
using Weftone.Domain;

namespace Weftone.App.Controls;

/// <summary>
/// 自动化 lane 自绘控件（SPEC M2）：折线 + 关键点圆点，首末点向两侧 clamp 平直延伸；
/// 与 Piano Roll 同横轴、横向滚动由宿主同步。手势三件套与音符一致：
/// 空处左键=画点、左键拖点=移动、右键点=删除；命令与事务全部经 MainWindowViewModel。
/// </summary>
public sealed class AutomationLaneControl : Control
{
    public static readonly StyledProperty<MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<AutomationLaneControl, MainWindowViewModel?>(nameof(ViewModel));

    public static readonly StyledProperty<Pattern?> PatternProperty =
        AvaloniaProperty.Register<AutomationLaneControl, Pattern?>(nameof(Pattern));

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x20));
    private static readonly IBrush BeatLineBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x30));
    private static readonly IBrush BarLineBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x42));
    private static readonly IPen BeatPen = new Pen(BeatLineBrush);
    private static readonly IPen BarPen = new Pen(BarLineBrush);
    private static readonly IPen CurvePen = new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0xD9, 0xC8)), 1.5);
    private static readonly IBrush PointBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0xF0, 0xE0));
    private static readonly IBrush GesturePointBrush = Brushes.White;
    private static readonly IPen PlayheadPen = new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0xF0, 0xE0), 0.5), 1);

    private bool _dragging;
    private double _viewportX;
    private double _viewportWidth;

    public MainWindowViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public Pattern? Pattern
    {
        get => GetValue(PatternProperty);
        set => SetValue(PatternProperty, value);
    }

    static AutomationLaneControl()
    {
        PatternProperty.Changed.AddClassHandler<AutomationLaneControl>(static (c, _) => c.InvalidateVisual());
    }

    /// <summary>由宿主（跟随 Piano Roll 滚动）同步可见视口，用于裁剪。</summary>
    public void SetViewport(double x, double width)
    {
        _viewportX = x;
        _viewportWidth = width;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = Math.Max(ContentWidth, _viewportWidth);
        return new Size(width, AutomationLaneGeometry.LaneHeight);
    }

    private double ContentWidth =>
        Pattern is null
            ? PianoRollGeometry.TickToX(TimeBase.Ppq * 16)
            : PianoRollGeometry.TickToX(PatternLengthTicks(Pattern));

    private static long PatternLengthTicks(Pattern pattern)
    {
        long end = 0;
        foreach (Note note in pattern.Notes)
        {
            end = Math.Max(end, note.Tick + note.LengthTicks);
        }
        long barTicks = (long)TimeBase.Ppq * 4;
        return Math.Max(barTicks * 4, (end + barTicks - 1) / barTicks * barTicks);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));

        (long firstTick, long lastTick) = PianoRollGeometry.VisibleTickRange(
            _viewportX, _viewportX + _viewportWidth);
        int ppq = TimeBase.Ppq;
        for (long beat = firstTick / ppq; beat <= lastTick / ppq; beat++)
        {
            double x = PianoRollGeometry.TickToX(beat * ppq);
            context.DrawLine(beat % 4 == 0 ? BarPen : BeatPen,
                new Point(x, 0), new Point(x, Bounds.Height));
        }

        MainWindowViewModel? vm = ViewModel;
        if (vm is null)
        {
            return;
        }
        IReadOnlyList<AutomationPoint> points = vm.CurrentAutomationLane;
        (double min, double max) = vm.CurrentAutomationRange;

        if (vm.IsPlaying)
        {
            double playX = PianoRollGeometry.TickToX(vm.PlayheadTick);
            if (playX >= 0 && playX <= Bounds.Width)
            {
                context.DrawLine(PlayheadPen, new Point(playX, 0), new Point(playX, Bounds.Height));
            }
        }

        if (points.Count == 0)
        {
            return;
        }
        List<AutomationPoint> visible = AutomationLaneGeometry.VisibleWithNeighbors(points, firstTick, lastTick);

        // 折线：首点前/末点后 clamp 平直延伸到内容边缘
        double firstX = PianoRollGeometry.TickToX(visible[0].Tick);
        double firstY = AutomationLaneGeometry.ValueToY(visible[0].Value, min, max);
        var polyline = new List<Point>
        {
            new(0, firstY),
            new(firstX, firstY),
        };
        for (int i = 1; i < visible.Count; i++)
        {
            polyline.Add(new Point(
                PianoRollGeometry.TickToX(visible[i].Tick),
                AutomationLaneGeometry.ValueToY(visible[i].Value, min, max)));
        }
        polyline.Add(new(Bounds.Width, polyline[^1].Y));
        context.DrawLine(CurvePen, polyline[0], polyline[1]);
        for (int i = 1; i < polyline.Count - 1; i++)
        {
            context.DrawLine(CurvePen, polyline[i], polyline[i + 1]);
        }

        foreach (AutomationPoint point in visible)
        {
            bool active = _dragging && vm.AutomationLiveTick == point.Tick;
            context.FillRectangle(active ? GesturePointBrush : PointBrush,
                new Rect(
                    PianoRollGeometry.TickToX(point.Tick) - AutomationLaneGeometry.PointRadius,
                    AutomationLaneGeometry.ValueToY(point.Value, min, max) - AutomationLaneGeometry.PointRadius,
                    AutomationLaneGeometry.PointRadius * 2,
                    AutomationLaneGeometry.PointRadius * 2),
                (float)AutomationLaneGeometry.PointRadius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        MainWindowViewModel? vm = ViewModel;
        if (vm?.SelectedAutomationTarget is null)
        {
            return;
        }
        (double min, double max) = vm.CurrentAutomationRange;
        Point point = e.GetPosition(this);
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        IReadOnlyList<AutomationPoint> lane = vm.CurrentAutomationLane;
        AutomationPoint? hit = AutomationLaneGeometry.HitTest(lane, point.X, point.Y, min, max);

        if (props.IsRightButtonPressed)
        {
            if (hit is not null)
            {
                vm.RemoveAutomationPoint(hit.Value.Tick);
                InvalidateVisual();
                e.Handled = true;
            }
            return;
        }
        if (!props.IsLeftButtonPressed)
        {
            return;
        }
        long tick = PianoRollGeometry.SnapTick(PianoRollGeometry.XToTick(point.X));
        double value = AutomationLaneGeometry.YToValue(point.Y, min, max);
        if (hit is not null)
        {
            vm.BeginDragAutomationPoint(hit.Value.Tick);
        }
        else
        {
            vm.BeginAddAutomationPoint(tick, value);
        }
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        MainWindowViewModel? vm = ViewModel;
        if (!_dragging || vm?.SelectedAutomationTarget is null)
        {
            return;
        }
        Point point = e.GetPosition(this);
        (double min, double max) = vm.CurrentAutomationRange;
        long tick = PianoRollGeometry.SnapTick(PianoRollGeometry.XToTick(point.X));
        double value = AutomationLaneGeometry.YToValue(point.Y, min, max);
        vm.DragAutomationPoint(Math.Max(0, tick), value);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            ViewModel?.EndAutomationGesture();
            InvalidateVisual();
        }
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}
