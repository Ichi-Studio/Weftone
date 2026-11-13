using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Weftone.App.ViewModels;
using Weftone.Domain;

namespace Weftone.App.Controls;

/// <summary>
/// Playlist 自绘控件（M1b D3）：一轨一 Pattern，clip 块 = pattern 内容长度；
/// 小节网格横向时间轴（复用 PianoRollGeometry tick 映射）。
/// 手势：空轨左键=放置 clip、拖 clip=移动（可换轨）、右键=删除；命令全部经 VM。
/// </summary>
public sealed class PlaylistControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<Pattern>?> PatternsProperty =
        AvaloniaProperty.Register<PlaylistControl, IReadOnlyList<Pattern>?>(nameof(Patterns));

    public static readonly StyledProperty<IReadOnlyList<Clip>?> PlaylistProperty =
        AvaloniaProperty.Register<PlaylistControl, IReadOnlyList<Clip>?>(nameof(Playlist));

    public static readonly StyledProperty<MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<PlaylistControl, MainWindowViewModel?>(nameof(ViewModel));

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x20));
    private static readonly IBrush LaneDividerBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14));
    private static readonly IBrush BeatLineBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x34));
    private static readonly IBrush BarLineBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x48));
    private static readonly IPen BeatPen = new Pen(BeatLineBrush);
    private static readonly IPen BarPen = new Pen(BarLineBrush);
    private static readonly IPen GesturePen = new Pen(Brushes.White, 1.5);

    private Clip? _gestureClip;
    private long _pressTick;
    private long _originTick;
    private int _originLane;
    private double _viewportWidth;

    public IReadOnlyList<Pattern>? Patterns
    {
        get => GetValue(PatternsProperty);
        set => SetValue(PatternsProperty, value);
    }

    public IReadOnlyList<Clip>? Playlist
    {
        get => GetValue(PlaylistProperty);
        set => SetValue(PlaylistProperty, value);
    }

    public MainWindowViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static PlaylistControl()
    {
        PatternsProperty.Changed.AddClassHandler<PlaylistControl>(static (c, _) => c.OnContentChanged());
        PlaylistProperty.Changed.AddClassHandler<PlaylistControl>(static (c, _) => c.OnContentChanged());
    }

    public void SetViewport(double width)
    {
        _viewportWidth = width;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnContentChanged()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int lanes = Math.Max(1, Patterns?.Count ?? 0);
        return new Size(Math.Max(ContentWidth, _viewportWidth), lanes * PlaylistGeometry.LaneHeight);
    }

    private double ContentWidth
    {
        get
        {
            double width = PianoRollGeometry.TickToX(TimeBase.Ppq * 16);
            IReadOnlyList<Clip>? clips = Playlist;
            IReadOnlyList<Pattern>? patterns = Patterns;
            if (clips is not null && patterns is not null)
            {
                foreach (Clip clip in clips)
                {
                    foreach (Pattern pattern in patterns)
                    {
                        if (pattern.Id == clip.PatternId)
                        {
                            width = Math.Max(width,
                                PlaylistGeometry.ClipLeft(clip) + PlaylistGeometry.ClipWidth(pattern) + 128);
                        }
                    }
                }
            }
            return width;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));
        IReadOnlyList<Pattern>? patterns = Patterns;
        IReadOnlyList<Clip>? clips = Playlist;
        if (patterns is null)
        {
            return;
        }

        int lanes = Math.Max(1, patterns.Count);
        for (int lane = 0; lane <= lanes; lane++)
        {
            double y = PlaylistGeometry.LaneTop(lane);
            context.DrawLine(new Pen(LaneDividerBrush), new Point(0, y), new Point(Bounds.Width, y));
        }

        (long firstTick, long lastTick) = PianoRollGeometry.VisibleTickRange(0, Bounds.Width);
        int ppq = TimeBase.Ppq;
        for (long beat = firstTick / ppq; beat <= lastTick / ppq; beat++)
        {
            double x = PianoRollGeometry.TickToX(beat * ppq);
            context.DrawLine(beat % 4 == 0 ? BarPen : BeatPen,
                new Point(x, 0), new Point(x, Bounds.Height));
        }

        if (clips is null)
        {
            return;
        }
        foreach (Clip clip in clips)
        {
            int lane = PlaylistGeometry.LaneOf(patterns, clip.PatternId);
            if (lane < 0)
            {
                continue;
            }
            Pattern pattern = patterns[lane];
            var rect = new Rect(
                PlaylistGeometry.ClipLeft(clip) + 1,
                PlaylistGeometry.LaneTop(lane) + 2,
                PlaylistGeometry.ClipWidth(pattern) - 2,
                PlaylistGeometry.LaneHeight - 4);
            context.FillRectangle(ClipBrush(lane), rect, 3);
            var label = new FormattedText(
                pattern.Name,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium),
                12, Brushes.Black)
            {
                TextAlignment = TextAlignment.Left,
            };
            if (label.Width <= rect.Width - 8)
            {
                context.DrawText(label, new Point(rect.X + 5, rect.Y + (rect.Height - label.Height) / 2));
            }
            if (ReferenceEquals(clip, _gestureClip))
            {
                context.DrawRectangle(GesturePen, rect, 3);
            }
        }
    }

    private static IBrush ClipBrush(int lane)
    {
        // 每轨一个色相（绕开暗色），黑字可读
        float hue = (35f + lane * 67f) % 360f;
        float c = 0.62f;
        float x = c * (1 - Math.Abs(hue / 60f % 2 - 1));
        float r = 0, g = 0, b = 0;
        switch ((int)(hue / 60f) % 6)
        {
            case 0: r = c; g = x; break;
            case 1: r = x; g = c; break;
            case 2: g = c; b = x; break;
            case 3: g = x; b = c; break;
            case 4: r = x; b = c; break;
            case 5: r = c; b = x; break;
        }
        return new SolidColorBrush(Color.FromArgb(255,
            (byte)(255 * (r + 0.38f)), (byte)(255 * (g + 0.38f)), (byte)(255 * (b + 0.38f))));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        MainWindowViewModel? vm = ViewModel;
        IReadOnlyList<Pattern>? patterns = Patterns;
        IReadOnlyList<Clip>? clips = Playlist;
        if (vm is null || patterns is null || clips is null || patterns.Count == 0)
        {
            return;
        }
        Point point = e.GetPosition(this);
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        ClipHit hit = PlaylistGeometry.HitTest(clips, patterns, point.X, point.Y);

        if (props.IsRightButtonPressed)
        {
            if (hit.Clip is not null)
            {
                vm.RemoveClip(hit.Clip);
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
        if (hit.Clip is not null)
        {
            vm.BeginMoveClip(hit.Clip);
            _gestureClip = hit.Clip;
            _pressTick = tick;
            _originTick = hit.Clip.StartTick;
            _originLane = PlaylistGeometry.LaneOf(patterns, hit.Clip.PatternId);
        }
        else
        {
            int lane = PlaylistGeometry.LaneAt(point.Y, patterns.Count);
            vm.AddClipAt(patterns[lane], tick);
            _gestureClip = vm.LastAddedClip;
            _pressTick = tick;
            _originTick = tick;
            _originLane = lane;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_gestureClip is null)
        {
            return;
        }
        MainWindowViewModel? vm = ViewModel;
        IReadOnlyList<Pattern>? patterns = Patterns;
        if (vm is null || patterns is null || patterns.Count == 0)
        {
            return;
        }
        Point point = e.GetPosition(this);
        long tick = PianoRollGeometry.SnapTick(PianoRollGeometry.XToTick(point.X));
        long delta = tick - _pressTick;
        long targetTick = Math.Max(0, PianoRollGeometry.SnapTick(_originTick) + delta);
        int targetLane = PlaylistGeometry.LaneAt(point.Y, patterns.Count);
        vm.DragClip(_gestureClip, targetTick, patterns[targetLane]);
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
        if (_gestureClip is null)
        {
            return;
        }
        _gestureClip = null;
        ViewModel?.EndClipGesture();
        InvalidateVisual();
    }
}
