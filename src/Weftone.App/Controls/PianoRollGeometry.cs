using Weftone.Domain;

namespace Weftone.App.Controls;

public enum NoteZone
{
    None,
    Body,
    RightEdge,
}

public readonly record struct NoteHit(Note? Note, NoteZone Zone);

/// <summary>
/// Piano Roll 几何/命中纯函数（M1b SPEC：不依赖 Avalonia 类型，headless 单测覆盖）。
/// 横轴 tick（PPQ=96）、纵轴 MIDI key（127 在顶），tick↔pixel 线性映射。
/// </summary>
public static class PianoRollGeometry
{
    public const double KeyHeight = 14;
    public const double PixelsPerBeat = 64;
    public const double EdgeZone = 6;
    public const int KeyCount = 128;
    /// <summary>默认吸附步长：1/4 拍（16 分音符）。</summary>
    public const int SnapTicks = TimeBase.Ppq / 4;

    public static double TotalHeight => KeyCount * KeyHeight;

    public static double TickToX(long tick) => tick * PixelsPerBeat / TimeBase.Ppq;

    public static long XToTick(double x) => (long)Math.Floor(x * TimeBase.Ppq / PixelsPerBeat);

    /// <summary>key 0..127，127 在顶部。</summary>
    public static double KeyToY(int key) => (KeyCount - 1 - key) * KeyHeight;

    public static int YToKey(double y) =>
        Math.Clamp(KeyCount - 1 - (int)Math.Floor(y / KeyHeight), 0, KeyCount - 1);

    public static long SnapTick(long tick) =>
        (long)Math.Round(tick / (double)SnapTicks, MidpointRounding.AwayFromZero) * SnapTicks;

    public static bool IsBlackKey(int key) => key % 12 is 1 or 3 or 6 or 8 or 10;

    /// <summary>命中测试：列表后画的在上层，逆序取最上层；音符右缘 EdgeZone 像素内为 RightEdge。</summary>
    public static NoteHit HitTest(IReadOnlyList<Note> notes, double x, double y)
    {
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            Note note = notes[i];
            double left = TickToX(note.Tick);
            double right = TickToX(note.Tick + note.LengthTicks);
            double top = KeyToY(note.Key);
            if (x < left || x > right || y < top || y >= top + KeyHeight)
            {
                continue;
            }
            if (right - x <= EdgeZone)
            {
                return new NoteHit(note, NoteZone.RightEdge);
            }
            return new NoteHit(note, NoteZone.Body);
        }
        return new NoteHit(null, NoteZone.None);
    }

    /// <summary>可见 tick 范围（两侧各留 1 tick 余量），First &lt;= Last。</summary>
    public static (long FirstTick, long LastTick) VisibleTickRange(double x0, double x1)
    {
        long first = Math.Max(0, XToTick(x0) - 1);
        long last = Math.Max(first, XToTick(x1) + 1);
        return (first, last);
    }

    /// <summary>可见 key 范围（y0 视口顶、y1 视口底），FirstKey &lt;= LastKey。</summary>
    public static (int FirstKey, int LastKey) VisibleKeyRange(double y0, double y1)
    {
        int topKey = YToKey(y0);
        int bottomKey = YToKey(y1);
        return (Math.Min(topKey, bottomKey), Math.Max(topKey, bottomKey));
    }
}
