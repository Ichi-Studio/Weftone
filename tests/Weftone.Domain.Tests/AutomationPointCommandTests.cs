using NUnit.Framework;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Domain.Tests;

/// <summary>SPEC M2 竖切 1：点命令往返/事务合并/同 tick 覆盖/值域拒绝。</summary>
public class AutomationPointCommandTests
{
    private ProjectDocument _document = null!;
    private CommandHistory _history = null!;

    [SetUp]
    public void SetUp()
    {
        _document = new ProjectDocument();
        _document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Sine });
        _document.Channels.Add(new Channel { Id = 2, Kind = ChannelKind.Sine });
        _history = new CommandHistory();
    }

    private static AutomationTarget Ch1 => AutomationTarget.Channel(1);

    private void AddPoint(long tick, double value, AutomationParameter parameter = AutomationParameter.Volume,
        AutomationTarget? target = null)
        => _history.Execute(new AddAutomationPointCommand(
            target ?? Ch1, parameter, new AutomationPoint { Tick = tick, Value = value }), _document);

    [Test]
    public void Add_keeps_lane_sorted_ascending()
    {
        AddPoint(192, 0.5);
        AddPoint(0, 0.1);
        AddPoint(96, 0.3);
        List<AutomationPoint> points = _document.Channels[0].VolumePoints;
        Assert.That(points.Select(p => p.Tick), Is.EqualTo(new long[] { 0, 96, 192 }));
        Assert.That(points.Select(p => p.Value), Is.EqualTo(new[] { 0.1, 0.3, 0.5 }));
    }

    [Test]
    public void Add_same_tick_replaces_and_undo_restores_previous()
    {
        AddPoint(96, 0.3);
        AddPoint(96, 1.2);
        Assert.That(_document.Channels[0].VolumePoints.Count, Is.EqualTo(1));
        Assert.That(_document.Channels[0].VolumePoints[0].Value, Is.EqualTo(1.2), "同 tick 后者覆盖");

        _history.Undo(_document);
        Assert.That(_document.Channels[0].VolumePoints[0].Value, Is.EqualTo(0.3), "撤销还原被替换点");
        _history.Redo(_document);
        Assert.That(_document.Channels[0].VolumePoints[0].Value, Is.EqualTo(1.2), "重做再次覆盖");
    }

    [Test]
    public void Add_rejects_out_of_range_values()
    {
        Assert.That(() => AddPoint(0, 2.5), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => AddPoint(0, -0.1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => AddPoint(0, 1.5, AutomationParameter.Pan), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => AddPoint(0, -1.5, AutomationParameter.Pan), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => AddPoint(-1, 0.5), Throws.TypeOf<ArgumentOutOfRangeException>(), "负 tick");
        Assert.That(_document.Channels[0].VolumePoints, Is.Empty, "拒绝的命令不留半态");
        Assert.That(_history.CanUndo, Is.False, "拒绝的命令不入栈");
    }

    [Test]
    public void Add_master_volume_lane_supported_but_master_pan_rejected()
    {
        AddPoint(96, 0.8, AutomationParameter.Volume, AutomationTarget.Master);
        Assert.That(_document.MasterVolumePoints[0].Value, Is.EqualTo(0.8));
        Assert.That(() => AutomationLanes.Points(_document, AutomationTarget.Master, AutomationParameter.Pan),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Unknown_channel_or_missing_point_throws()
    {
        Assert.That(() => AutomationLanes.Points(_document, AutomationTarget.Channel(99), AutomationParameter.Volume),
            Throws.InvalidOperationException);
        Assert.That(() => _history.Execute(
            new RemoveAutomationPointCommand(Ch1, AutomationParameter.Volume, 96), _document),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Remove_and_revert_roundtrip()
    {
        AddPoint(0, 0.1);
        AddPoint(96, 0.3);
        _history.Execute(new RemoveAutomationPointCommand(Ch1, AutomationParameter.Volume, 0), _document);
        Assert.That(_document.Channels[0].VolumePoints.Select(p => p.Tick), Is.EqualTo(new long[] { 96 }));

        _history.Undo(_document);
        Assert.That(_document.Channels[0].VolumePoints.Select(p => p.Tick), Is.EqualTo(new long[] { 0, 96 }));
        Assert.That(_document.Channels[0].VolumePoints[0].Value, Is.EqualTo(0.1));
    }

    [Test]
    public void Move_replaces_victim_at_target_tick_and_revert_restores_both()
    {
        AddPoint(0, 0.1);
        AddPoint(96, 0.3);
        _history.Execute(new MoveAutomationPointCommand(
            Ch1, AutomationParameter.Volume, currentTick: 0, newTick: 96, newValue: 0.9), _document);

        List<AutomationPoint> points = _document.Channels[0].VolumePoints;
        Assert.That(points.Count, Is.EqualTo(1), "目标 tick 已有点被替换");
        Assert.That(points[0].Tick, Is.EqualTo(96));
        Assert.That(points[0].Value, Is.EqualTo(0.9));

        _history.Undo(_document);
        points = _document.Channels[0].VolumePoints;
        Assert.That(points.Count, Is.EqualTo(2), "被移点与被替换点都还原");
        Assert.That(points[0], Is.EqualTo(new AutomationPoint { Tick = 0, Value = 0.1 }));
        Assert.That(points[1], Is.EqualTo(new AutomationPoint { Tick = 96, Value = 0.3 }));
    }

    [Test]
    public void Move_missing_source_point_throws()
    {
        AddPoint(0, 0.1);
        Assert.That(() => _history.Execute(new MoveAutomationPointCommand(
            Ch1, AutomationParameter.Volume, currentTick: 48, newTick: 96, newValue: 0.5), _document),
            Throws.InvalidOperationException);
        Assert.That(_document.Channels[0].VolumePoints[0].Tick, Is.EqualTo(0), "失败不留半态");
    }

    [Test]
    public void Drag_gesture_merges_into_single_undo_step()
    {
        _document.Channels[0].VolumePoints.Add(new AutomationPoint { Tick = 0, Value = 0.5 });

        _history.BeginTransaction("拖点");
        _history.Execute(new MoveAutomationPointCommand(Ch1, AutomationParameter.Volume, 0, 24, 0.6), _document);
        _history.Execute(new MoveAutomationPointCommand(Ch1, AutomationParameter.Volume, 24, 48, 0.7), _document);
        _history.Execute(new MoveAutomationPointCommand(Ch1, AutomationParameter.Volume, 48, 96, 0.8), _document);
        _history.EndTransaction();
        Assert.That(_document.Channels[0].VolumePoints, Has.Count.EqualTo(1));
        Assert.That(_document.Channels[0].VolumePoints[0], Is.EqualTo(new AutomationPoint { Tick = 96, Value = 0.8 }));

        _history.Undo(_document);
        Assert.That(_document.Channels[0].VolumePoints, Has.Count.EqualTo(1), "一步撤销整段手势");
        Assert.That(_document.Channels[0].VolumePoints[0], Is.EqualTo(new AutomationPoint { Tick = 0, Value = 0.5 }));
    }

    [Test]
    public void Move_across_lanes_is_isolated()
    {
        AddPoint(96, 0.3, AutomationParameter.Volume);
        AddPoint(96, -0.5, AutomationParameter.Pan);
        _history.Execute(new MoveAutomationPointCommand(
            Ch1, AutomationParameter.Pan, currentTick: 96, newTick: 192, newValue: 0.5), _document);
        Assert.Multiple(() =>
        {
            Assert.That(_document.Channels[0].PanPoints[0], Is.EqualTo(new AutomationPoint { Tick = 192, Value = 0.5 }));
            Assert.That(_document.Channels[0].VolumePoints[0].Tick, Is.EqualTo(96), "volume lane 不受影响");
        });
    }
}
