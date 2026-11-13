using Weftone.Abstractions;
using Weftone.Abstractions.Rendering;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.Application;

/// <summary>
/// 编辑会话（M1b）：UI 线程单写者持有 Document + 命令日志 + 资产。
/// 命令执行后由调用方全量编译（M1b 文档规模小，同步）；RenderPlan 交 PlaybackService。
/// 资产源文件存于临时工作区，Save 时写入 zip；载入新工程清空撤销栈。
/// </summary>
public sealed class EditSession : IDisposable
{
    private readonly IProjectStore _store;
    private readonly ISampleDecoder _decoder;
    private readonly IResampler _resampler;
    private readonly string _workspace;
    private readonly Dictionary<string, string> _assetSources = new();
    private List<PreparedAsset> _prepared = [];
    private bool _disposed;

    public EditSession(IProjectStore store, ISampleDecoder decoder, IResampler resampler)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(resampler);
        _store = store;
        _decoder = decoder;
        _resampler = resampler;
        _workspace = Path.Combine(Path.GetTempPath(), "weftone-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        Document = new ProjectDocument();
    }

    public ProjectDocument Document { get; private set; }

    public CommandHistory History { get; } = new();

    public bool IsDirty { get; private set; }

    public string? ProjectPath { get; private set; }

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
        IsDirty = true;
    }

    public void Open(string path)
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
        PrepareAssets();
        History.Clear();
        ProjectPath = path;
        IsDirty = false;
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _store.Save(path, Document, _assetSources);
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
        IsDirty = true;
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
        IsDirty = true;
    }

    public void Redo()
    {
        History.Redo(Document);
        IsDirty = true;
    }

    private void PrepareAssets()
    {
        _prepared = new AssetPreparer().Prepare(
            Document, asset => File.OpenRead(_assetSources[asset.Path]), _decoder, _resampler);
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