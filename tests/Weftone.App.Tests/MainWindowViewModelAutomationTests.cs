using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Engine;

namespace Weftone.App.Tests;

/// <summary>SPEC M2 竖切 2：VM 自动化手势（画/拖/删 + 事务合并单步撤销）、目标选择、只读显示值。</summary>
public class MainWindowViewModelAutomationTests
{
    private static MainWindowViewModel NewViewModel(out EditSession session, out Channel channel)
    {
        session = new EditSession(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());
        ProjectDocument document = session.Document;
        channel = new Channel { Id = 1, Name = "Lead", Kind = ChannelKind.Sine, Volume = 0.5, Pan = 0 };
        document.Channels.Add(channel);
        document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        var vm = new MainWindowViewModel(session, playback, new FakeDialogs())
        {
            SelectedPattern = document.Patterns[0],
        };
        return vm;
    }

    [Test]
    public void Automation_targets_cover_channel_volume_pan_and_master()
    {
        MainWindowViewModel vm = NewViewModel(out _, out _);
        Assert.That(vm.AutomationTargets.Select(t => t.Label),
            Is.EqualTo(new[] { "Lead · 音量", "Lead · 声像", "Master · 音量" }));
    }

    [Test]
    public void Add_point_gesture_merges_add_and_drag_into_single_undo()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out Channel channel);
        vm.SelectedAutomationTarget = vm.AutomationTargets[0]; // Lead 音量

        vm.BeginAddAutomationPoint(96, 0.25);
        Assert.That(channel.VolumePoints, Has.Count.EqualTo(1));
        vm.DragAutomationPoint(192, 0.75);
        vm.DragAutomationPoint(288, 1.5);
        vm.EndAutomationGesture();

        Assert.Multiple(() =>
        {
            Assert.That(channel.VolumePoints, Has.Count.EqualTo(1), "拖动同一点不增点");
            Assert.That(channel.VolumePoints[0], Is.EqualTo(new AutomationPoint { Tick = 288, Value = 1.5 }));
        });

        vm.UndoCommand.Execute(null);
        Assert.That(channel.VolumePoints, Is.Empty, "画点+拖动合并为一步撤销");
        Assert.That(session.History.CanUndo, Is.False);
        vm.RedoCommand.Execute(null);
        Assert.That(channel.VolumePoints[0].Tick, Is.EqualTo(288), "重做恢复");
    }

    [Test]
    public void Drag_existing_point_merges_steps_and_replaces_victim()
    {
        MainWindowViewModel vm = NewViewModel(out _, out Channel channel);
        vm.SelectedAutomationTarget = vm.AutomationTargets[0];
        vm.BeginAddAutomationPoint(0, 0.1);
        vm.EndAutomationGesture();
        vm.BeginAddAutomationPoint(96, 0.3);
        vm.EndAutomationGesture();
        vm.UndoCommand.Execute(null); // 清掉第二条
        vm.UndoCommand.Execute(null); // 清掉第一条，lane 手动预置
        channel.VolumePoints.Add(new AutomationPoint { Tick = 0, Value = 0.1 });
        channel.VolumePoints.Add(new AutomationPoint { Tick = 96, Value = 0.3 });

        vm.BeginDragAutomationPoint(0);
        vm.DragAutomationPoint(96, 0.9); // 目标 tick 已有点 → 替换
        vm.EndAutomationGesture();
        Assert.Multiple(() =>
        {
            Assert.That(channel.VolumePoints, Has.Count.EqualTo(1));
            Assert.That(channel.VolumePoints[0], Is.EqualTo(new AutomationPoint { Tick = 96, Value = 0.9 }));
        });

        vm.UndoCommand.Execute(null);
        Assert.That(channel.VolumePoints.Select(p => (p.Tick, p.Value)),
            Is.EqualTo(new (long, double)[] { (0, 0.1), (96, 0.3) }), "单步撤销还原被移点与被替换点");
    }

    [Test]
    public void Remove_point_is_immediate_and_undoable()
    {
        MainWindowViewModel vm = NewViewModel(out _, out Channel channel);
        vm.SelectedAutomationTarget = vm.AutomationTargets[1]; // Lead 声像
        vm.BeginAddAutomationPoint(0, -0.5);
        vm.EndAutomationGesture();

        vm.RemoveAutomationPoint(0);
        Assert.That(channel.PanPoints, Is.Empty);
        vm.UndoCommand.Execute(null);
        Assert.That(channel.PanPoints[0].Value, Is.EqualTo(-0.5));
    }

    [Test]
    public void Master_target_writes_master_lane()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out _);
        vm.SelectedAutomationTarget = vm.AutomationTargets[^1]; // Master 音量
        vm.BeginAddAutomationPoint(192, 1.25);
        vm.EndAutomationGesture();
        Assert.That(session.Document.MasterVolumePoints[0], Is.EqualTo(new AutomationPoint { Tick = 192, Value = 1.25 }));
        Assert.That(vm.HasMasterAutomation, Is.True);
    }

    [Test]
    public void Display_value_follows_curve_with_clamp_and_falls_back_to_static()
    {
        MainWindowViewModel vm = NewViewModel(out _, out Channel channel);
        Assert.That(vm.GetAutomationDisplayValue(channel, AutomationParameter.Volume), Is.EqualTo(0.5), "无曲线回退静态值");

        channel.VolumePoints.Add(new AutomationPoint { Tick = 96, Value = 0.0 });
        channel.VolumePoints.Add(new AutomationPoint { Tick = 384, Value = 1.6 });
        // 播放头在 0（未播放）：首点前 clamp 到首值
        Assert.That(vm.GetAutomationDisplayValue(channel, AutomationParameter.Volume), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(vm.HasMasterAutomation, Is.False);
    }

    [Test]
    public void Gesture_without_target_is_ignored()
    {
        MainWindowViewModel vm = NewViewModel(out EditSession session, out _);
        vm.BeginAddAutomationPoint(0, 0.5);
        vm.DragAutomationPoint(96, 0.5);
        vm.EndAutomationGesture();
        Assert.Multiple(() =>
        {
            Assert.That(session.Document.Channels[0].VolumePoints, Is.Empty);
            Assert.That(session.History.CanUndo, Is.False, "无目标不产生命令");
            Assert.That(vm.AutomationLiveTick, Is.Null);
        });
    }
}
