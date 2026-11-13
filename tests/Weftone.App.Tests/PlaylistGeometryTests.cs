using NUnit.Framework;
using Weftone.App.Controls;
using Weftone.Domain;

namespace Weftone.App.Tests;

public class PlaylistGeometryTests
{
    private static Pattern Pattern(int id, string name, long contentTicks = 96)
    {
        var pattern = new Pattern { Id = id, Name = name };
        if (contentTicks > 0)
        {
            pattern.Notes.Add(new Note { ChannelId = 1, Tick = 0, Key = 60, LengthTicks = contentTicks });
        }
        return pattern;
    }

    [Test]
    public void Lane_of_maps_pattern_id_to_index()
    {
        var patterns = new System.Collections.Generic.List<Pattern> { Pattern(10, "P1"), Pattern(11, "P2") };
        Assert.Multiple(() =>
        {
            Assert.That(PlaylistGeometry.LaneOf(patterns, 10), Is.EqualTo(0));
            Assert.That(PlaylistGeometry.LaneOf(patterns, 11), Is.EqualTo(1));
            Assert.That(PlaylistGeometry.LaneOf(patterns, 99), Is.EqualTo(-1));
        });
    }

    [Test]
    public void Lane_at_clamps_to_valid_range()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlaylistGeometry.LaneAt(0, 3), Is.EqualTo(0));
            Assert.That(PlaylistGeometry.LaneAt(PlaylistGeometry.LaneHeight * 2 + 5, 3), Is.EqualTo(2));
            Assert.That(PlaylistGeometry.LaneAt(-20, 3), Is.EqualTo(0), "越界钳制");
            Assert.That(PlaylistGeometry.LaneAt(9999, 3), Is.EqualTo(2), "越界钳制");
        });
    }

    [Test]
    public void Clip_width_covers_content_plus_one_beat()
    {
        Pattern pattern = Pattern(10, "P1", contentTicks: 96);
        Assert.That(PlaylistGeometry.ClipWidth(pattern),
            Is.EqualTo(PianoRollGeometry.TickToX(96 + TimeBase.Ppq)), "内容 + 1 拍余量");
        Pattern empty = Pattern(11, "P2", contentTicks: 0);
        Assert.That(PlaylistGeometry.ClipWidth(empty),
            Is.EqualTo(PianoRollGeometry.TickToX(TimeBase.Ppq)), "空 pattern 至少 1 拍");
    }

    [Test]
    public void Hit_test_finds_clip_in_its_lane_and_none_outside()
    {
        var patterns = new System.Collections.Generic.List<Pattern> { Pattern(10, "P1"), Pattern(11, "P2") };
        var clips = new System.Collections.Generic.List<Clip>
        {
            new() { PatternId = 10, StartTick = 0 },
        };
        double inLane = PlaylistGeometry.LaneHeight / 2;
        double x = PianoRollGeometry.TickToX(48);

        Assert.That(PlaylistGeometry.HitTest(clips, patterns, x, inLane).Clip, Is.SameAs(clips[0]));
        Assert.That(PlaylistGeometry.HitTest(clips, patterns, x, PlaylistGeometry.LaneHeight + inLane).Clip,
            Is.Null, "第二轨无 clip");
        Assert.That(PlaylistGeometry.HitTest(
            clips, patterns, PianoRollGeometry.TickToX(96 + TimeBase.Ppq + 96), inLane).Clip,
            Is.Null, "clip 宽度之外");
    }

    [Test]
    public void Hit_test_prefers_topmost_clip()
    {
        var patterns = new System.Collections.Generic.List<Pattern> { Pattern(10, "P1") };
        var bottom = new Clip { PatternId = 10, StartTick = 0 };
        var top = new Clip { PatternId = 10, StartTick = 0 };
        var clips = new System.Collections.Generic.List<Clip> { bottom, top };
        ClipHit hit = PlaylistGeometry.HitTest(clips, patterns,
            PianoRollGeometry.TickToX(48), PlaylistGeometry.LaneHeight / 2);
        Assert.That(hit.Clip, Is.SameAs(top), "后画的在上层");
    }
}
