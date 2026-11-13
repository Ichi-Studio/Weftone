using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Weftone.App.ViewModels;
using Weftone.Domain;

namespace Weftone.App.Controls;

/// <summary>
/// Piano Roll 自绘控件（M1b D3=Avalonia DrawingContext）：小节/拍网格 + 音符矩形（力度色），
/// 视口裁剪只画可见范围（1 万音符流畅）。手势：空处左拖=画音符、拖身体=移动、拖右缘=改长度、
/// 右键=删除；命令与事务全部经 MainWindowViewModel（VM 纯 C# 可 headless 测试）。
/// </summary>
public sealed class PianoRollControl : Control
{
    public static readonly StyledProperty<Pattern?> PatternProperty =
        AvaloniaProperty.Register<PianoRollControl, Pattern?>(nameof(Pattern));

    public static readonly StyledProperty<MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<PianoRollControl, MainWindowViewModel?>(nameof(ViewModel));

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24));
    private static readonly IBrush BlackRowBrush = new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x2B));
    private static readonly IBrush BeatLineBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x34));
    private static readonly IBrush BarLineBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x48));
    private static readonly IPen BeatPen = new Pen(BeatLineBrush);
    private static readonly IPen BarPen = new Pen(BarLineBrush);
    private static readonly IPen GesturePen = new Pen(Brushes.White, 1.5);

    private enum Gesture
    {
        None,
        AddResize,
        Move,
        Resize,
    }

    private Gesture _gesture;
    private Note? _gestureNote;
    private long _pressTick;
    private long _originTick;
    private double _viewportX;
    private double _viewportY;
    private double _viewportWidth;
    private double _viewportHeight;

    public Pattern? Pattern
    {
        get => GetValue(PatternProperty);
        set => SetValue(PatternProperty, value);
    }

    public MainWindowViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static PianoRollControl()
    {
        PatternProperty.Changed.AddClassHandler<PianoRollControl>(static (c, _) => c.OnPatternChanged());
    }

    /// <summary>由宿主（ScrollViewer 联动）同步可见视口，用于裁剪。</summary>
    public void SetViewport(double x, double y, double width, double height)
    {
        _viewportX = x;
        _viewportY = y;
        _viewportWidth = width;
        _viewportHeight = height;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnPatternChanged()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = Math.Max(ContentWidth, _viewportWidth);
        return new Size(width, PianoRollGeometry.TotalHeight);
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
        Pattern? pattern = Pattern;
        if (pattern is null)
        {
            return;
        }

        (long firstTick, long lastTick) = PianoRollGeometry.VisibleTickRange(
            _viewportX, _viewportX + _viewportWidth);
        (int firstKey, int lastKey) = PianoRollGeometry.VisibleKeyRange(
            _viewportY, _viewportY + _viewportHeight);

        for (int key = firstKey; key <= lastKey; key++)
        {
            if (!PianoRollGeometry.IsBlackKey(key))
            {
                continue;
            }
            double y = PianoRollGeometry.KeyToY(key);
            context.FillRectangle(BlackRowBrush,
                new Rect(0, y, Bounds.Width, PianoRollGeometry.KeyHeight));
        }

        int ppq = TimeBase.Ppq;
        long firstBeat = firstTick / ppq;
        long lastBeat = lastTick / ppq;
        for (long beat = firstBeat; beat <= lastBeat; beat++)
        {
            double x = PianoRollGeometry.TickToX(beat * ppq);
            bool bar = beat % 4 == 0;
            context.DrawLine(bar ? BarPen : BeatPen,
                new Point(x, 0), new Point(x, Bounds.Height));
        }

        foreach (Note note in pattern.Notes)
        {
            if (note.Tick + note.LengthTicks < firstTick || note.Tick > lastTick)
            {
                continue;
            }
            double left = PianoRollGeometry.TickToX(note.Tick);
            double width = Math.Max(3, PianoRollGeometry.TickToX(note.LengthTicks) - 2);
            double top = PianoRollGeometry.KeyToY(note.Key);
            var rect = new Rect(left + 1, top + 1, width, PianoRollGeometry.KeyHeight - 2);
            context.FillRectangle(NoteBrush(note.Velocity), rect, 2);
            if (ReferenceEquals(note, _gestureNote))
            {
                context.DrawRectangle(GesturePen, rect, 2);
            }
        }
    }

    private static IBrush NoteBrush(double velocity)
    {
        // 力度色：低=蓝 → 高=橙（HSV 210→30）
        float hue = (float)(210 - 180 * Math.Clamp(velocity, 0, 1));
        return new SolidColorBrush(ColorFromHsv(hue, 0.72f, 0.92f));
    }

    private static Color ColorFromHsv(float hue, float saturation, float value)
    {
        float c = value * saturation;
        float h = hue / 60f;
        float x = c * (1 - Math.Abs(h % 2 - 1));
        float r = 0, g = 0, b = 0;
        switch ((int)h % 6)
        {
            case 0: r = c; g = x; break;
            case 1: r = x; g = c; break;
            case 2: g = c; b = x; break;
            case 3: g = x; b = c; break;
            case 4: r = x; b = c; break;
            case 5: r = c; b = x; break;
        }
        float m = value - c;
        return Color.FromArgb(255, (byte)(255 * (r + m)), (byte)(255 * (g + m)), (byte)(255 * (b + m)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        MainWindowViewModel? vm = ViewModel;
        Pattern? pattern = Pattern;
        if (vm is null || pattern is null)
        {
            return;
        }
        Point point = e.GetPosition(this);
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        NoteHit hit = PianoRollGeometry.HitTest(pattern.Notes, point.X, point.Y);

        if (props.IsRightButtonPressed)
        {
            if (hit.Note is not null)
            {
                vm.RemoveNote(hit.Note);
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
        int key = PianoRollGeometry.YToKey(point.Y);
        switch (hit.Zone)
        {
            case NoteZone.RightEdge:
                vm.BeginResizeNote(hit.Note!);
                _gesture = Gesture.Resize;
                _gestureNote = hit.Note;
                _pressTick = tick;
                _originTick = hit.Note!.Tick;
                break;
            case NoteZone.Body:
                vm.BeginMoveNote(hit.Note!);
                _gesture = Gesture.Move;
                _gestureNote = hit.Note;
                _pressTick = tick;
                _originTick = hit.Note!.Tick;
                break;
            default:
                vm.BeginAddNote(tick, key);
                _gesture = Gesture.AddResize;
                _gestureNote = vm.LastAddedNote;
                _pressTick = tick;
                _originTick = tick;
                break;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_gesture == Gesture.None || _gestureNote is null)
        {
            return;
        }
        MainWindowViewModel? vm = ViewModel;
        if (vm is null)
        {
            return;
        }
        Point point = e.GetPosition(this);
        long tick = PianoRollGeometry.SnapTick(PianoRollGeometry.XToTick(point.X));
        switch (_gesture)
        {
            case Gesture.Move:
                long delta = tick - _pressTick;
                long targetTick = Math.Max(0, PianoRollGeometry.SnapTick(_originTick) + delta);
                vm.DragNoteMove(_gestureNote, targetTick, PianoRollGeometry.YToKey(point.Y));
                break;
            case Gesture.Resize:
            case Gesture.AddResize:
                long endTick = Math.Max(tick, _originTick + TimeBase.Ppq / 4);
                vm.DragNoteLength(_gestureNote, endTick - _originTick);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndGesture();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void EndGesture()
    {
        if (_gesture == Gesture.None)
        {
            return;
        }
        _gesture = Gesture.None;
        _gestureNote = null;
        ViewModel?.EndNoteGesture();
        InvalidateVisual();
    }
}
