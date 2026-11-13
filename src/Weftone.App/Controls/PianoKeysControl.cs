using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Weftone.App.Controls;

/// <summary>
/// 钢琴键侧栏（M1b D3 自绘）：128 键竖排，纵向偏移与 Piano Roll 同步滚动；C 键标八度。
/// </summary>
public sealed class PianoKeysControl : Control
{
    public static readonly StyledProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.Register<PianoKeysControl, double>(nameof(VerticalOffset));

    private static readonly IBrush WhiteKeyBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE8));
    private static readonly IBrush BlackKeyBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x33));
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x58));
    private static readonly IBrush SeparatorBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x6A));

    public double VerticalOffset
    {
        get => GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    static PianoKeysControl()
    {
        VerticalOffsetProperty.Changed.AddClassHandler<PianoKeysControl>(
            static (c, _) => c.InvalidateVisual());
    }

    public PianoKeysControl()
    {
        Width = 72;
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(72, PianoRollGeometry.TotalHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        (int firstKey, int lastKey) = PianoRollGeometry.VisibleKeyRange(
            VerticalOffset, VerticalOffset + Bounds.Height);
        double bottomY = VerticalOffset + Bounds.Height;

        for (int key = firstKey; key <= lastKey; key++)
        {
            double y = PianoRollGeometry.KeyToY(key) - VerticalOffset;
            double height = PianoRollGeometry.KeyHeight;
            if (y + height <= 0 || y >= bottomY)
            {
                continue;
            }
            bool black = PianoRollGeometry.IsBlackKey(key);
            double width = black ? 46 : 72;
            context.FillRectangle(black ? BlackKeyBrush : WhiteKeyBrush,
                new Rect(72 - width, y, width, height - 1));
            context.FillRectangle(DividerBrush, new Rect(0, y, 72, 1));
            if (!black && key % 12 == 0)
            {
                int octave = key / 12 - 1;
                var label = new FormattedText(
                    $"C{octave}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium),
                    10, LabelBrush)
                {
                    TextAlignment = TextAlignment.Left,
                };
                context.DrawText(label, new Point(4, y + 1.5));
            }
        }
        context.FillRectangle(SeparatorBrush, new Rect(71, 0, 1, Bounds.Height));
    }
}
