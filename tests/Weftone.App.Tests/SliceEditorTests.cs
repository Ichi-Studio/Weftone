using NUnit.Framework;
using Weftone.App.Controls;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Engine;
using Weftone.Domain;

namespace Weftone.App.Tests;

/// <summary>SPEC M4 竖切 3：波形/切片几何纯函数 + 通道属性与切片点手势（VM 事务撤销）。</summary>
public class SliceEditorTests
{
    [Test]
    public void Frame_x_mapping_roundtrips()
    {
        const long frames = 4_800;
        const double width = 300;
        Assert.Multiple(() =>
        {
            Assert.That(WaveformGeometry.FrameToX(0, frames, width), Is.EqualTo(0).Within(1e-9));
            Assert.That(WaveformGeometry.FrameToX(frames, frames, width), Is.EqualTo(width).Within(1e-9));
            Assert.That(WaveformGeometry.XToFrame(width / 2, frames, width), Is.EqualTo(2_400));
            Assert.That(WaveformGeometry.XToFrame(-100, frames, width), Is.EqualTo(0), "越界 clamp");
            Assert.That(WaveformGeometry.XToFrame(9_999, frames, width), Is.EqualTo(frames));
        });
    }

    [Test]
    public void Slice_hit_test_finds_nearest_within_radius()
    {
        long[] slices = [0, 1_200, 2_400];
        double x1200 = WaveformGeometry.FrameToX(1_200, 4_800, 300);
        Assert.Multiple(() =>
        {
            Assert.That(WaveformGeometry.HitTestSlice(slices, x1200 + 2, 4_800, 300), Is.EqualTo(1_200));
            Assert.That(WaveformGeometry.HitTestSlice(slices, x1200 + 60, 4_800, 300), Is.Null, "超出半径");
            Assert.That(WaveformGeometry.HitTestSlice([], 10, 4_800, 300), Is.Null);
        });
    }

    private static MainWindowViewModel NewViewModel(out EditSession session, out Channel channel)
    {
        session = new EditSession(new Weftone.Adapters.ProjectStore.WeftProjectStore(),
            new Weftone.Adapters.Codecs.DrWavDecoder(), new Weftone.Adapters.Codecs.LinearResampler(),
            new Weftone.Engine.PlanRenderer());
        channel = new Channel { Id = 1, Name = "Kick", Kind = ChannelKind.Sampler, AssetId = "a" };
        session.Document.Channels.Add(channel);
        session.Document.Assets.Add(new AssetRef { Id = "a", Path = "assets/a.wav" });
        var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        return new MainWindowViewModel(session, playback, new FakeDialogs());
    }

    [Test]
    public void Stretch_speed_command_is_undoable_and_snaps_to_step()
    {
        MainWindowViewModel vm = NewViewModel(out _, out Channel channel);
        vm.SetChannelStretchSpeed(channel, 1.4);
        Assert.That(channel.StretchSpeed, Is.EqualTo(1.5), "近邻档位");
        vm.UndoCommand.Execute(null);
        Assert.That(channel.StretchSpeed, Is.EqualTo(1.0));
    }

    [Test]
    public void Slice_mode_toggle_is_undoable_and_clears_stretch()
    {
        MainWindowViewModel vm = NewViewModel(out _, out Channel channel);
        vm.SetChannelStretchSpeed(channel, 1.5);
        vm.SetChannelSliceMode(channel, true);
        Assert.Multiple(() =>
        {
            Assert.That(channel.SliceMode, Is.True);
            Assert.That(channel.StretchSpeed, Is.EqualTo(1.0), "切片与拉伸互斥");
        });
        vm.UndoCommand.Execute(null);
        Assert.That(channel.SliceMode, Is.False);
    }

    [Test]
    public void Slice_point_drag_merges_into_single_undo()
    {
        MainWindowViewModel vm = NewViewModel(out _, out _);
        vm.AddSlicePoint("a", 100);

        vm.BeginSlicePointDrag();
        vm.DragSlicePoint("a", 100, 200);
        vm.DragSlicePoint("a", 200, 300);
        vm.EndSlicePointDrag();
        Assert.That(vm.GetSlicePoints("a"), Is.EqualTo(new long[] { 300 }));

        vm.UndoCommand.Execute(null);
        Assert.That(vm.GetSlicePoints("a"), Is.EqualTo(new long[] { 100 }), "整段拖拽一步撤销回到拖拽前");
        vm.UndoCommand.Execute(null);
        Assert.That(vm.GetSlicePoints("a"), Is.Empty, "再撤一步移除切片点");
    }
}
