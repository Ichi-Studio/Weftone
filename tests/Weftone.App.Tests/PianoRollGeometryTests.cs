using NUnit.Framework;
using Weftone.App.Controls;
using Weftone.Domain;

namespace Weftone.App.Tests;

public class PianoRollGeometryTests
{
    [Test]
    public void Tick_x_mapping_roundtrips()
    {
        Assert.That(PianoRollGeometry.TickToX(TimeBase.Ppq), Is.EqualTo(PianoRollGeometry.PixelsPerBeat));
        Assert.That(PianoRollGeometry.XToTick(PianoRollGeometry.PixelsPerBeat), Is.EqualTo(TimeBase.Ppq));
        Assert.That(PianoRollGeometry.XToTick(PianoRollGeometry.PixelsPerBeat - 0.01),
            Is.EqualTo(TimeBase.Ppq - 1), "截断向下");
    }

    [Test]
    public void Key_y_mapping_places_127_on_top()
    {
        Assert.That(PianoRollGeometry.KeyToY(127), Is.EqualTo(0));
        Assert.That(PianoRollGeometry.KeyToY(0), Is.EqualTo(PianoRollGeometry.TotalHeight - PianoRollGeometry.KeyHeight));
        Assert.That(PianoRollGeometry.YToKey(0), Is.EqualTo(127));
        Assert.That(PianoRollGeometry.YToKey(PianoRollGeometry.KeyHeight), Is.EqualTo(126));
        Assert.That(PianoRollGeometry.YToKey(-50), Is.EqualTo(127), "越界钳制");
        Assert.That(PianoRollGeometry.YToKey(999_999), Is.EqualTo(0), "越界钳制");
    }

    [Test]
    public void Snap_rounds_to_quarter_beat()
    {
        Assert.That(PianoRollGeometry.SnapTick(0), Is.EqualTo(0));
        Assert.That(PianoRollGeometry.SnapTick(11), Is.EqualTo(0));
        Assert.That(PianoRollGeometry.SnapTick(12), Is.EqualTo(24), "半步远离零取整");
        Assert.That(PianoRollGeometry.SnapTick(36), Is.EqualTo(48));
        Assert.That(PianoRollGeometry.SnapTick(97), Is.EqualTo(96));
    }

    [Test]
    public void Hit_test_returns_body_right_edge_or_none()
    {
        var note = new Note { ChannelId = 1, Tick = 0, Key = 60, LengthTicks = TimeBase.Ppq };
        var notes = new System.Collections.Generic.List<Note> { note };
        double midY = PianoRollGeometry.KeyToY(60) + PianoRollGeometry.KeyHeight / 2;
        double rightX = PianoRollGeometry.TickToX(TimeBase.Ppq);

        Assert.That(PianoRollGeometry.HitTest(notes, 32, midY).Zone, Is.EqualTo(NoteZone.Body));
        Assert.That(PianoRollGeometry.HitTest(notes, rightX - 2, midY).Zone, Is.EqualTo(NoteZone.RightEdge));
        Assert.That(PianoRollGeometry.HitTest(notes, rightX + 2, midY).Zone, Is.EqualTo(NoteZone.None));
        Assert.That(PianoRollGeometry.HitTest(notes, 32, midY + PianoRollGeometry.KeyHeight).Zone,
            Is.EqualTo(NoteZone.None), "纵向越界");
    }

    [Test]
    public void Hit_test_prefers_topmost_note()
    {
        var bottom = new Note { ChannelId = 1, Tick = 0, Key = 60, LengthTicks = TimeBase.Ppq };
        var top = new Note { ChannelId = 1, Tick = 0, Key = 60, LengthTicks = TimeBase.Ppq, Velocity = 0.5 };
        var notes = new System.Collections.Generic.List<Note> { bottom, top };
        double midY = PianoRollGeometry.KeyToY(60) + PianoRollGeometry.KeyHeight / 2;
        NoteHit hit = PianoRollGeometry.HitTest(notes, 32, midY);
        Assert.That(hit.Note, Is.SameAs(top), "后画的在上层");
    }

    [Test]
    public void Visible_ranges_cover_viewport_with_margin()
    {
        (long firstTick, long lastTick) = PianoRollGeometry.VisibleTickRange(0, 640);
        Assert.Multiple(() =>
        {
            Assert.That(firstTick, Is.EqualTo(0));
            Assert.That(lastTick, Is.EqualTo(TimeBase.Ppq * 10 + 1), "640px = 10 拍 + 1 tick 余量");
        });

        (int firstKey, int lastKey) = PianoRollGeometry.VisibleKeyRange(0, 560);
        Assert.Multiple(() =>
        {
            Assert.That(lastKey, Is.EqualTo(127));
            Assert.That(firstKey, Is.EqualTo(127 - 40), "560px = 40 行");
        });
    }
}
