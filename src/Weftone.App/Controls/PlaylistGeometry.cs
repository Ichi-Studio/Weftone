using Weftone.Domain;

namespace Weftone.App.Controls;

public enum ClipZone
{
    None,
    Body,
}

public readonly record struct ClipHit(Clip? Clip, ClipZone Zone);

/// <summary>
/// Playlist 几何/命中纯函数（M1b SPEC）：横轴复用 PianoRollGeometry 的 tick↔pixel 映射，
/// 纵轴一轨一 Pattern（轨序 = Patterns 列表序）。headless 单测覆盖。
/// </summary>
public static class PlaylistGeometry
{
    public const double LaneHeight = 28;

    /// <summary>clip 显示宽度：pattern 内容长度 + 1 拍余量（FL 风格：clip 包住内容）。</summary>
    public static double ClipWidth(Pattern pattern) =>
        PianoRollGeometry.TickToX(PatternContentTicks(pattern));

    public static double ClipLeft(Clip clip) => PianoRollGeometry.TickToX(clip.StartTick);

    public static double LaneTop(int laneIndex) => laneIndex * LaneHeight;

    /// <summary>命中测试：列表后画的在上层，逆序取最上层。</summary>
    public static ClipHit HitTest(IReadOnlyList<Clip> clips, IReadOnlyList<Pattern> patterns,
        double x, double y)
    {
        for (int i = clips.Count - 1; i >= 0; i--)
        {
            Clip clip = clips[i];
            int lane = LaneOf(patterns, clip.PatternId);
            if (lane < 0)
            {
                continue;
            }
            double left = ClipLeft(clip);
            double right = left + ClipWidth(PatternOf(patterns, clip.PatternId)!);
            double top = LaneTop(lane);
            if (x >= left && x < right && y >= top && y < top + LaneHeight)
            {
                return new ClipHit(clip, ClipZone.Body);
            }
        }
        return new ClipHit(null, ClipZone.None);
    }

    /// <summary>y 坐标所在轨序；越界返回最近轨（钳制）。</summary>
    public static int LaneAt(double y, int laneCount) =>
        Math.Clamp((int)Math.Floor(y / LaneHeight), 0, Math.Max(0, laneCount - 1));

    /// <summary>PatternId → 轨序；未知返回 -1。</summary>
    public static int LaneOf(IReadOnlyList<Pattern> patterns, int patternId)
    {
        for (int i = 0; i < patterns.Count; i++)
        {
            if (patterns[i].Id == patternId)
            {
                return i;
            }
        }
        return -1;
    }

    private static Pattern? PatternOf(IReadOnlyList<Pattern> patterns, int patternId)
    {
        for (int i = 0; i < patterns.Count; i++)
        {
            if (patterns[i].Id == patternId)
            {
                return patterns[i];
            }
        }
        return null;
    }

    private static long PatternContentTicks(Pattern pattern)
    {
        long end = 0;
        foreach (Note note in pattern.Notes)
        {
            end = Math.Max(end, note.Tick + note.LengthTicks);
        }
        return Math.Max(TimeBase.Ppq, end + TimeBase.Ppq);
    }
}
