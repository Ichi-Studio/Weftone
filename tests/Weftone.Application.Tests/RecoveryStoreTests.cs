using NUnit.Framework;
using Weftone.Adapters.Codecs;
using Weftone.Adapters.ProjectStore;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Domain.Commands;
using Weftone.Engine;

namespace Weftone.Application.Tests;

/// <summary>SPEC M2 竖切 3：恢复区原子写、快照策略、往返与清理。</summary>
public class RecoveryStoreTests
{
    private string _workDir = "";
    private string _recoveryDir = "";

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "weftone-recovery-" + Guid.NewGuid().ToString("N"));
        _recoveryDir = Path.Combine(_workDir, "recovery");
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_workDir, recursive: true);

    private static EditSession NewSession()
        => new(new WeftProjectStore(), new DrWavDecoder(), new LinearResampler(), new PlanRenderer());

    private static void AddSineContent(EditSession session, double volume = 1.0)
    {
        session.Document.Channels.Add(new Channel { Id = 1, Kind = ChannelKind.Sine, Volume = volume });
        session.Document.Patterns.Add(new Pattern { Id = 10, Name = "P1" });
        session.Document.Patterns[0].Notes.Add(
            new Note { ChannelId = 1, Tick = 0, Key = 69, Velocity = 0.8, LengthTicks = 96 });
        session.Document.Playlist.Add(new Clip { PatternId = 10, StartTick = 0 });
    }

    [Test]
    public void Policy_triggers_on_idle_or_command_count_and_requires_dirty()
    {
        DateTime now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        Assert.Multiple(() =>
        {
            Assert.That(AutosavePolicy.ShouldSnapshot(false, now - TimeSpan.FromSeconds(99), 99, now), Is.False, "干净不触发");
            Assert.That(AutosavePolicy.ShouldSnapshot(true, null, 99, now), Is.False, "无编辑时间不触发");
            Assert.That(AutosavePolicy.ShouldSnapshot(true, now, 0, now), Is.False, "刚编辑且未到阈值");
            Assert.That(AutosavePolicy.ShouldSnapshot(true, now - TimeSpan.FromSeconds(5), 0, now), Is.True, "空闲 ≥5s");
            Assert.That(AutosavePolicy.ShouldSnapshot(true, now - TimeSpan.FromSeconds(1), 50, now), Is.True, "≥50 次编辑");
            Assert.That(AutosavePolicy.ShouldSnapshot(true, now - TimeSpan.FromSeconds(1), 49, now), Is.False, "未到任一阈值");
        });
    }

    [Test]
    public void Session_counts_edits_and_resets_on_snapshot()
    {
        using EditSession session = NewSession();
        Assert.That(session.EditsSinceSnapshot, Is.EqualTo(0), "初始无编辑");
        session.Execute(new SetTempoCommand(120));
        session.Undo();
        session.Redo();
        Assert.That(session.EditsSinceSnapshot, Is.EqualTo(3), "命令 + 撤销 + 重做");
        session.MarkSnapshotted();
        Assert.That(session.EditsSinceSnapshot, Is.EqualTo(0));
    }

    [Test]
    public void Untitled_snapshot_roundtrips_full_document()
    {
        using EditSession session = NewSession();
        AddSineContent(session, volume: 0.75);
        AutomationLanes.Insert(session.Document.Channels[0].VolumePoints,
            new AutomationPoint { Tick = 96, Value = 1.25 });
        var store = new RecoveryStore(_recoveryDir);

        RecoverySnapshot snapshot = store.Save(session);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SourceProjectPath, Is.Null, "未保存工程");
            Assert.That(Directory.GetFiles(_recoveryDir, "*.tmp"), Is.Empty, "提交后无 tmp 残留");
            Assert.That(session.EditsSinceSnapshot, Is.EqualTo(0), "快照后计数清零");
            Assert.That(session.IsDirty, Is.True, "快照不改脏标记");
        });
        using EditSession restored = NewSession();
        restored.Open(snapshot.SnapshotPath);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Document.Channels[0].Volume, Is.EqualTo(0.75));
            Assert.That(restored.Document.Channels[0].VolumePoints[0],
                Is.EqualTo(new AutomationPoint { Tick = 96, Value = 1.25 }), "自动化 lane 随快照往返");
            Assert.That(restored.IsDirty, Is.False, "普通打开不算脏");
        });
    }

    [Test]
    public void Named_project_snapshot_keeps_source_path_and_slot_replaces()
    {
        string projectPath = Path.Combine(_workDir, "song.weft");
        using (EditSession session = NewSession())
        {
            AddSineContent(session);
            session.Save(projectPath);
            var store = new RecoveryStore(_recoveryDir);
            store.Save(session);
            // 二次编辑后再快照：同槽替换，不累积文件
            session.Execute(new SetTempoCommand(100));
            store.Save(session);
            Assert.That(Directory.GetFiles(_recoveryDir, "*.weft"), Has.Length.EqualTo(1));
        }
        var reader = new RecoveryStore(_recoveryDir);
        RecoverySnapshot found = reader.FindAll().Single();
        Assert.That(found.SourceProjectPath, Is.EqualTo(projectPath));
        Assert.That(found.SnapshotPath, Does.EndWith(RecoveryStore.KeyFor(projectPath) + ".weft"));
    }

    /// <summary>SPEC：写一半模拟崩溃 → 旧快照完好（tmp 与正式文件隔离，File.Replace 原子生效）。</summary>
    [Test]
    public void Interrupted_tmp_write_never_corrupts_previous_snapshot()
    {
        using EditSession session = NewSession();
        AddSineContent(session, volume: 0.5);
        var store = new RecoveryStore(_recoveryDir);
        store.Save(session);

        string snapshotFile = Directory.GetFiles(_recoveryDir, "*.weft").Single();
        string tmpFile = snapshotFile + ".tmp";
        File.WriteAllBytes(tmpFile, [0x50, 0x4B, 0x03, 0x04, 0xDE, 0xAD]); // 模拟写一半崩溃的残片

        using EditSession check = NewSession();
        check.Open(snapshotFile);
        Assert.That(check.Document.Channels[0].Volume, Is.EqualTo(0.5), "旧快照完好可加载");

        session.Execute(new SetTempoCommand(100));
        store.Save(session);
        using EditSession second = NewSession();
        second.Open(snapshotFile);
        Assert.Multiple(() =>
        {
            Assert.That(second.Document.Tempo, Is.EqualTo(100), "重存后拿到新内容");
            Assert.That(Directory.GetFiles(_recoveryDir, "*.tmp"), Is.Empty, "tmp 已被替换消化");
        });
    }

    [Test]
    public void Save_cleanup_removes_untitled_and_own_slot_only()
    {
        string songA = Path.Combine(_workDir, "a.weft");
        string songB = Path.Combine(_workDir, "b.weft");
        using EditSession sessionA = NewSession();
        AddSineContent(sessionA);
        sessionA.Save(songA);
        new RecoveryStore(_recoveryDir).Save(sessionA);

        using EditSession sessionB = NewSession();
        AddSineContent(sessionB);
        sessionB.Save(songB);
        new RecoveryStore(_recoveryDir).Save(sessionB);

        using EditSession untitled = NewSession();
        AddSineContent(untitled);
        new RecoveryStore(_recoveryDir).Save(untitled);
        Assert.That(new RecoveryStore(_recoveryDir).FindAll(), Has.Count.EqualTo(3));

        var store = new RecoveryStore(_recoveryDir);
        store.RemoveStale(songA);
        IReadOnlyList<RecoverySnapshot> rest = store.FindAll();
        Assert.That(rest.Select(s => s.SourceProjectPath), Is.EqualTo(new string?[] { songB }),
            "清掉 untitled 与自身槽，其他工程的快照保留");
    }

    [Test]
    public void Clear_all_empties_recovery_directory()
    {
        using EditSession session = NewSession();
        AddSineContent(session);
        var store = new RecoveryStore(_recoveryDir);
        store.Save(session);
        store.ClearAll();
        Assert.Multiple(() =>
        {
            Assert.That(store.FindAll(), Is.Empty);
            Assert.That(Directory.GetFiles(_recoveryDir), Is.Empty);
        });
    }
}
