using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Weftone.Domain;

namespace Weftone.Application;

/// <summary>恢复区快照描述（SPEC M2 D4=a）。</summary>
public sealed record RecoverySnapshot(string SnapshotPath, string? SourceProjectPath, DateTime SavedAtUtc);

/// <summary>
/// 恢复区管理（SPEC M2 D4=a）：用户级目录（%LocalAppData%\Weftone\recovery），
/// 每个打开工程一个快照槽（key = 工程路径 SHA256，未保存工程 = "untitled"）。
/// 原子写：*.tmp → File.Replace——中断的临时写永不破坏上一次好快照。
/// </summary>
public sealed class RecoveryStore
{
    private const string UntitledKey = "untitled";

    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public RecoveryStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Weftone", "recovery");
    }

    public string Directory { get; }

    /// <summary>全量快照当前会话（含资产），不改 ProjectPath/IsDirty，写完重置编辑计数。</summary>
    public RecoverySnapshot Save(EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string key = session.ProjectPath is null ? UntitledKey : KeyFor(session.ProjectPath);
        string finalPath = Path.Combine(Directory, key + ".weft");
        string metaPath = Path.Combine(Directory, key + ".json");
        System.IO.Directory.CreateDirectory(Directory);
        var savedAt = DateTime.UtcNow;
        WriteAtomic(metaPath, tmp =>
        {
            using FileStream stream = File.Create(tmp);
            JsonSerializer.Serialize(stream, new RecoveryMeta
            {
                SourcePath = session.ProjectPath,
                SavedAtUtc = savedAt,
            }, MetaJsonOptions);
        });
        WriteAtomic(finalPath, tmp => session.SnapshotTo(tmp));
        session.MarkSnapshotted();
        return new RecoverySnapshot(finalPath, session.ProjectPath, savedAt);
    }

    public IReadOnlyList<RecoverySnapshot> FindAll()
    {
        var snapshots = new List<RecoverySnapshot>();
        if (!System.IO.Directory.Exists(Directory))
        {
            return snapshots;
        }
        foreach (string weft in System.IO.Directory.EnumerateFiles(Directory, "*.weft"))
        {
            string metaPath = Path.ChangeExtension(weft, ".json");
            if (!File.Exists(metaPath))
            {
                continue;
            }
            try
            {
                using FileStream stream = File.OpenRead(metaPath);
                RecoveryMeta? meta = JsonSerializer.Deserialize<RecoveryMeta>(stream, MetaJsonOptions);
                if (meta is not null)
                {
                    snapshots.Add(new RecoverySnapshot(weft, meta.SourcePath, meta.SavedAtUtc));
                }
            }
            catch (JsonException)
            {
                // 元数据损坏的快照忽略；主文件保留待用户丢弃清理
            }
        }
        return snapshots;
    }

    public void Remove(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        File.Delete(snapshot.SnapshotPath);
        File.Delete(Path.ChangeExtension(snapshot.SnapshotPath, ".json"));
    }

    /// <summary>显式保存成功后清理：未保存槽（untitled）+ 该工程路径槽。</summary>
    public void RemoveStale(string projectPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        RemoveByKey(UntitledKey);
        RemoveByKey(KeyFor(projectPath));
    }

    /// <summary>正常退出清理全部恢复项（SPEC：崩溃无此调用，快照存活供下次恢复）。</summary>
    public void ClearAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }
        foreach (string file in System.IO.Directory.EnumerateFiles(Directory))
        {
            File.Delete(file);
        }
    }

    private void RemoveByKey(string key)
    {
        File.Delete(Path.Combine(Directory, key + ".weft"));
        File.Delete(Path.Combine(Directory, key + ".json"));
    }

    private void WriteAtomic(string finalPath, Action<string> write)
    {
        string tmpPath = finalPath + ".tmp";
        write(tmpPath);
        if (File.Exists(finalPath))
        {
            File.Replace(tmpPath, finalPath, null);
        }
        else
        {
            File.Move(tmpPath, finalPath);
        }
    }

    public static string KeyFor(string projectPath)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(projectPath))));
}

/// <summary>快照触发策略（SPEC M2：编辑后空闲 ≥5s 或距上次快照 ≥50 次命令）。</summary>
public static class AutosavePolicy
{
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    public const int CommandThreshold = 50;

    public static bool ShouldSnapshot(EditSession session, DateTime nowUtc)
        => ShouldSnapshot(session.IsDirty, session.LastEditedAtUtc, session.EditsSinceSnapshot, nowUtc);

    public static bool ShouldSnapshot(bool isDirty, DateTime? lastEditedAtUtc, int editsSinceSnapshot, DateTime nowUtc)
        => isDirty
           && lastEditedAtUtc is { } edited
           && (nowUtc - edited >= IdleDelay || editsSinceSnapshot >= CommandThreshold);
}

internal sealed class RecoveryMeta
{
    [JsonPropertyName("sourcePath")] public string? SourcePath { get; set; }
    [JsonPropertyName("savedAtUtc")] public DateTime SavedAtUtc { get; set; }
}
