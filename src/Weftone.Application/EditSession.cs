using Weftone.Abstractions;
using Weftone.Abstractions.Plugins;
using Weftone.Abstractions.Rendering;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Application;

/// <summary>
/// 编辑会话（M1b）：UI 线程单写者持有 Document + 命令日志 + 资产。
/// 命令执行后由调用方全量编译（M1b 文档规模小，同步）；RenderPlan 交 PlaybackService。
/// 资产源文件存于临时工作区，Save 时写入 zip；载入新工程清空撤销栈。
/// offlineRenderer 用于导出（独立于实时播放的渲染器实例，经端口注入）。
/// </summary>
public sealed class EditSession : IDisposable
{
    private const int ExportBlockFrames = 512;

    private readonly IProjectStore _store;
    private readonly ISampleDecoder _decoder;
    private readonly IResampler _resampler;
    private readonly IPlanRenderer _offlineRenderer;
    private readonly IPluginInstanceFactory? _pluginFactory;
    private readonly Weftone.Abstractions.Dsp.ITimeStretcher? _stretcher;
    private readonly string _workspace;
    private readonly Dictionary<string, string> _assetSources = new();
    private List<PreparedAsset> _prepared = [];
    private bool _disposed;

    public EditSession(IProjectStore store, ISampleDecoder decoder, IResampler resampler,
        IPlanRenderer offlineRenderer, IPluginInstanceFactory? pluginFactory = null,
        Weftone.Abstractions.Dsp.ITimeStretcher? stretcher = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(resampler);
        ArgumentNullException.ThrowIfNull(offlineRenderer);
        _store = store;
        _decoder = decoder;
        _resampler = resampler;
        _offlineRenderer = offlineRenderer;
        _pluginFactory = pluginFactory;
        _stretcher = stretcher;
        _workspace = Path.Combine(Path.GetTempPath(), "weftone-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        Document = new ProjectDocument();
    }

    public ProjectDocument Document { get; private set; }

    public CommandHistory History { get; } = new();

    public bool IsDirty { get; private set; }

    public string? ProjectPath { get; private set; }

    /// <summary>最近一次编辑时间（autosave 空闲判定用，SPEC M2）。</summary>
    public DateTime? LastEditedAtUtc { get; private set; }

    /// <summary>距上次快照的编辑次数（autosave 计数触发用）。</summary>
    public int EditsSinceSnapshot { get; private set; }

    public void MarkSnapshotted() => EditsSinceSnapshot = 0;

    /// <summary>恢复区快照写（SPEC M2）：全量含资产，不改 ProjectPath/IsDirty。</summary>
    public void SnapshotTo(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _store.Save(path, Document, _assetSources);
    }

    private void NoteEdited()
    {
        IsDirty = true;
        LastEditedAtUtc = DateTime.UtcNow;
        EditsSinceSnapshot++;
    }

    public void NewDemo()
    {
        ResetWorkspace();
        Document = DemoProject.CreateDocument();
        string kickPath = Path.Combine(_workspace, "kick.wav");
        DemoProject.WriteKickWav(kickPath);
        _assetSources["assets/kick.wav"] = kickPath;
        PrepareAssets();
        History.Clear();
        ProjectPath = null;
        NoteEdited();
    }

    /// <summary>打开工程包。projectPathOverride 用于恢复区还原：从快照装载但记原工程路径并保持脏标记。</summary>
    /// <summary>资产波形峰值（SPEC M4 竖切 3 切片编辑面板）：每 peakStride 帧取最大绝对值。</summary>
    public float[] GetAssetPeaks(string assetId, int peakStride = 256)
    {
        PreparedAsset? asset = _prepared.FirstOrDefault(a => a.AssetId == assetId);
        if (asset is null || asset.Interleaved.Length == 0)
        {
            return [];
        }
        int channels = Math.Max(1, asset.Channels);
        long frames = asset.Frames;
        int count = (int)Math.Max(1, (frames + peakStride - 1) / peakStride);
        var peaks = new float[count];
        for (int p = 0; p < count; p++)
        {
            long start = (long)p * peakStride;
            long end = Math.Min(frames, start + peakStride);
            float peak = 0;
            for (long f = start; f < end; f++)
            {
                for (int c = 0; c < channels; c++)
                {
                    float magnitude = Math.Abs(asset.Interleaved[(int)(f * channels + c)]);
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }
            }
            peaks[p] = peak;
        }
        return peaks;
    }

    /// <summary>插件 state chunk（SPEC M3 竖切 3）：Open 时从包读入；Save 前由宿主抓取更新（写 presets/）。</summary>
    public IReadOnlyDictionary<int, byte[]> PluginStates { get; set; } = new Dictionary<int, byte[]>();

    public void Open(string path, string? projectPathOverride = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        LoadedProject loaded = _store.Load(path);
        ResetWorkspace();
        Document = loaded.Document;
        foreach (AssetRef asset in Document.Assets)
        {
            string localPath = Path.Combine(_workspace, Path.GetFileName(asset.Path));
            using (Stream source = loaded.OpenAsset(asset))
            using (FileStream target = File.Create(localPath))
            {
                source.CopyTo(target);
            }
            _assetSources[asset.Path] = localPath;
        }
        var states = new Dictionary<int, byte[]>();
        if (loaded.OpenEntry is not null)
        {
            foreach (Channel channel in Document.Channels)
            {
                if (string.IsNullOrEmpty(channel.PluginStateEntry))
                {
                    continue;
                }
                using Stream stream = loaded.OpenEntry(channel.PluginStateEntry);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                states[channel.Id] = memory.ToArray();
            }
        }
        PluginStates = states;
        PrepareAssets();
        History.Clear();
        ProjectPath = projectPathOverride ?? path;
        IsDirty = projectPathOverride is not null;
        EditsSinceSnapshot = projectPathOverride is not null ? 1 : 0;
        LastEditedAtUtc = projectPathOverride is not null ? DateTime.UtcNow : null;
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Dictionary<int, string>? stateFiles = null;
        if (PluginStates.Count > 0)
        {
            stateFiles = new Dictionary<int, string>();
            foreach (KeyValuePair<int, byte[]> pair in PluginStates)
            {
                string file = Path.Combine(_workspace, $"state-{pair.Key}.bin");
                File.WriteAllBytes(file, pair.Value);
                stateFiles[pair.Key] = file;
            }
        }
        _store.Save(path, Document, _assetSources, stateFiles);
        ProjectPath = path;
        IsDirty = false;
    }

    public RenderPlan Compile() => new DocumentCompiler().Compile(Document, _prepared);

    /// <summary>Pattern 模式编译：单个 Pattern + 循环区间（M1b）。</summary>
    public RenderPlan CompilePattern(int patternId) =>
        new DocumentCompiler().CompilePattern(Document, patternId, _prepared);

    public void Execute(ICommand command)
    {
        History.Execute(command, Document);
        NoteEdited();
    }

    /// <summary>开启拖拽事务：手势内的多条命令合并为单条撤销记录。</summary>
    public void BeginTransaction(string title)
    {
        History.BeginTransaction(title);
        IsDirty = true;
    }

    /// <summary>结束事务并入栈；空事务不入栈。</summary>
    public void EndTransaction()
    {
        History.EndTransaction();
        IsDirty = true;
    }

    public void Undo()
    {
        History.Undo(Document);
        NoteEdited();
    }

    public void Redo()
    {
        History.Redo(Document);
        NoteEdited();
    }

    /// <summary>
    /// 导入采样（spec §工程存取与采样导入）：解码校验 → 拷入工作区 →
    /// ImportSampleCommand 注册 AssetRef + sampler 通道（一步撤销）→ 重备资产。
    /// 返回新建通道；解码失败抛 InvalidDataException，不入命令栈。
    /// </summary>
    public Channel ImportSample(string wavPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(wavPath);
        string fileName = Path.GetFileName(wavPath);
        using (FileStream stream = File.OpenRead(wavPath))
        {
            _decoder.Decode(stream);
        }
        string assetId = "asset-" + Guid.NewGuid().ToString("N");
        string extension = Path.GetExtension(fileName);
        string entryPath = $"assets/{assetId}{extension}";
        string localPath = Path.Combine(_workspace, assetId + extension);
        File.Copy(wavPath, localPath, overwrite: true);

        int channelId = Document.Channels.Count == 0
            ? 1
            : Document.Channels.Max(c => c.Id) + 1;
        var channel = new Channel
        {
            Id = channelId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            Kind = ChannelKind.Sampler,
            AssetId = assetId,
        };
        Execute(new ImportSampleCommand(channel, new AssetRef { Id = assetId, Path = entryPath }));
        _assetSources[entryPath] = localPath;
        PrepareAssets();
        return channel;
    }

    /// <summary>
    /// 导出 WAV（spec §导出 WAV）：Song 全时间轴离线渲染（与实时播放互不干扰），
    /// IEEE float32 立体声。空时间轴写 0 帧 WAV。插件工程经临时实例表渲染（用后即毁，单线程安全）。
    /// </summary>
    public long ExportWav(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        RenderPlan plan = new DocumentCompiler().Compile(Document, _prepared);
        IReadOnlyList<Abstractions.Plugins.IAudioPluginInstance>? table = null;
        PluginInstanceHost? host = null;
        if (plan.PluginSlots.Length > 0 && _pluginFactory is not null)
        {
            host = new PluginInstanceHost(_pluginFactory);
            table = host.Sync(plan);
        }
        try
        {
            _offlineRenderer.Load(plan, table);
            using FileStream stream = File.Create(path);
            // WavWriter 的 totalSamples 语义 = 交错样本总数（帧 × 声道），与 CLI 导出一致
            WavWriter.WriteHeader(stream, plan.SampleRate, plan.Channels, plan.TotalSamples * plan.Channels);
            var block = new float[ExportBlockFrames * plan.Channels];
            for (long done = 0; done < plan.TotalSamples; done += ExportBlockFrames)
            {
                int count = (int)Math.Min(ExportBlockFrames, plan.TotalSamples - done);
                Span<float> span = block.AsSpan(0, count * plan.Channels);
                _offlineRenderer.Render(span);
                WavWriter.WriteSamples(stream, span);
            }
            return plan.TotalSamples;
        }
        finally
        {
            host?.Dispose();
        }
    }

    private void PrepareAssets()
    {
        _prepared = new AssetPreparer().Prepare(
            Document, asset => File.OpenRead(_assetSources[asset.Path]), _decoder, _resampler, _stretcher);
    }

    private void ResetWorkspace()
    {
        foreach (string file in Directory.EnumerateFiles(_workspace))
        {
            File.Delete(file);
        }
        _assetSources.Clear();
        _prepared = [];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            Directory.Delete(_workspace, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时工作区清理尽力而为，文件被占用时不阻断退出
        }
    }
}