using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.App.ViewModels;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Domain.Commands;
using Weftone.Engine;

namespace Weftone.App.Tests;

/// <summary>SPEC M2 竖切 3：启动恢复提示流（检测→恢复/丢弃）与 autosave tick 触发。</summary>
public class MainWindowViewModelRecoveryTests
{
    private string _workDir = "";
    private string _recoveryDir = "";

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "weftone-vmrecovery-" + Guid.NewGuid().ToString("N"));
        _recoveryDir = Path.Combine(_workDir, "recovery");
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_workDir, recursive: true);

    private static EditSession NewSession()
        => new(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());

    /// <summary>模拟崩溃遗留：会话 A 编辑后留下快照，然后进程死亡（未 Save、未 ClearAll）。</summary>
    private RecoverySnapshot LeaveCrashSnapshot(double volume, string? projectPath)
    {
        using EditSession crashed = NewSession();
        crashed.Document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Sine, Volume = volume });
        crashed.Document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        if (projectPath is not null)
        {
            crashed.Save(projectPath);
        }
        return new RecoveryStore(_recoveryDir).Save(crashed);
    }

    private MainWindowViewModel NewViewModel(EditSession session, RecoveryStore recovery)
    {
        var playback = new PlaybackService(() => new FakeBackend(), new PlanRenderer());
        return new MainWindowViewModel(session, playback, new FakeDialogs(), recovery);
    }

    [Test]
    public void Startup_detects_snapshot_and_restore_loads_document_with_original_path()
    {
        string songPath = Path.Combine(_workDir, "song.weft");
        LeaveCrashSnapshot(volume: 0.42, songPath);

        using EditSession session = NewSession();
        var recovery = new RecoveryStore(_recoveryDir);
        MainWindowViewModel vm = NewViewModel(session, recovery);
        Assert.That(vm.HasPendingRecovery, Is.False, "未检测前无提示");

        vm.CheckRecoveryOnStartup();
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasPendingRecovery, Is.True, "恢复区有快照 → 提示");
            Assert.That(vm.PendingRecovery!.SourceProjectPath, Is.EqualTo(songPath));
        });

        vm.RestoreRecoveryCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasPendingRecovery, Is.False, "恢复后提示消失");
            Assert.That(session.Document.Channels[0].Volume, Is.EqualTo(0.42), "装回崩溃时内容");
            Assert.That(session.ProjectPath, Is.EqualTo(songPath), "记原工程路径");
            Assert.That(session.IsDirty, Is.True, "恢复的内容仍是未保存状态");
            Assert.That(recovery.FindAll(), Has.Count.EqualTo(1), "快照保留至下次显式保存（防二次崩溃）");
        });
    }

    [Test]
    public void Discard_removes_snapshot_and_keeps_current_document()
    {
        LeaveCrashSnapshot(volume: 0.9, projectPath: null);

        using EditSession session = NewSession();
        var recovery = new RecoveryStore(_recoveryDir);
        MainWindowViewModel vm = NewViewModel(session, recovery);
        vm.CheckRecoveryOnStartup();
        Assert.That(vm.HasPendingRecovery, Is.True);

        vm.DiscardRecoveryCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasPendingRecovery, Is.False);
            Assert.That(recovery.FindAll(), Is.Empty, "快照文件被删除");
            Assert.That(session.Document.Channels, Is.Empty, "当前工程不受影响");
        });
    }

    [Test]
    public void Autosave_tick_snapshots_after_command_threshold()
    {
        using EditSession session = NewSession();
        session.Document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Sine });
        var recovery = new RecoveryStore(_recoveryDir);
        MainWindowViewModel vm = NewViewModel(session, recovery);

        vm.AutosaveTick();
        Assert.That(recovery.FindAll(), Is.Empty, "无编辑不快照");

        for (int i = 0; i < AutosavePolicy.CommandThreshold; i++)
        {
            session.Execute(new SetTempoCommand(120));
        }
        vm.AutosaveTick();
        Assert.That(recovery.FindAll(), Has.Count.EqualTo(1), "达到命令阈值 → 写恢复快照");
        Assert.That(session.EditsSinceSnapshot, Is.EqualTo(0), "计数重置");
    }

    [Test]
    public void Explicit_save_clears_recovery_slot()
    {
        string songPath = Path.Combine(_workDir, "saved.weft");
        LeaveCrashSnapshot(volume: 0.5, songPath);

        using EditSession session = NewSession();
        session.NewDemo();
        var recovery = new RecoveryStore(_recoveryDir);
        MainWindowViewModel vm = NewViewModel(session, recovery);

        // SaveAsync 走 SaveProjectAsync 对话框：ProjectPath 已设则直接保存
        session.Open(songPath);
        vm.SaveCommand.Execute(null);
        Assert.That(recovery.FindAll(), Is.Empty, "显式保存后清理恢复项");
    }

    [Test]
    public void Exit_clears_all_snapshots()
    {
        LeaveCrashSnapshot(volume: 0.5, projectPath: null);
        using EditSession session = NewSession();
        var recovery = new RecoveryStore(_recoveryDir);
        MainWindowViewModel vm = NewViewModel(session, recovery);

        vm.ClearRecoveryOnExit();
        Assert.That(recovery.FindAll(), Is.Empty, "正常退出清空恢复区（崩溃无此调用）");
    }
}
