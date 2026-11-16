using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weftone.Abstractions.Plugins;
using Weftone.Abstractions.Rendering;
using Weftone.App.Services;
using Weftone.Application;
using Weftone.Domain;
using Weftone.Domain.Commands;

namespace Weftone.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string DefaultPositionText = "0:00.000";

    private enum NoteGesture
    {
        None,
        AddResize,
        Move,
        Resize,
    }

    /// <summary>自动化 lane 目标项（SPEC M2 D3=a + M3 竖切 4 插件参数）；
    /// 插件参数带描述值域（Min/Max），其余走 AutomationLanes.Range。</summary>
    public sealed record AutomationTargetItem(
        AutomationTarget Target, AutomationParameter Parameter, string Label, double? Min = null, double? Max = null);

    private readonly EditSession _session;
    private readonly PlaybackService _playback;
    private readonly IFileDialogService _dialogs;
    private readonly RecoveryStore? _recovery;
    private readonly PluginParameterCatalog? _pluginCatalog;
    private bool _sliceGesture;
    private bool _pluginParamGesture;
    private Channel? _pluginParamChannel;
    private uint _pluginParamId;
    private double _pluginParamFrom;
    private NoteGesture _gesture;
    private Note? _gestureNote;
    private long _gestureFromTick;
    private long _gestureFromLength;
    private int _gestureFromKey;
    private bool _clipGesture;
    private long _clipFromTick;
    private int _clipFromPatternId;
    private bool _autoGesture;
    private long _autoLiveTick;

    [ObservableProperty]
    private string _title = "Weftone";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _positionText = DefaultPositionText;

    [ObservableProperty]
    private int _tempo;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private Pattern? _selectedPattern;

    [ObservableProperty]
    private bool _isSongMode;

    [ObservableProperty]
    private bool _isAutomationVisible;

    [ObservableProperty]
    private AutomationTargetItem? _selectedAutomationTarget;

    /// <summary>启动检测到的可恢复快照（null=无提示）；恢复/丢弃后清空（SPEC M2 D4=a）。</summary>
    [ObservableProperty]
    private RecoverySnapshot? _pendingRecovery;

    public bool HasPendingRecovery => PendingRecovery is not null;

    /// <summary>最近一次 BeginAddNote 创建的音符（控件手势回引用）。</summary>
    public Note? LastAddedNote { get; private set; }

    /// <summary>最近一次 AddClipAt 创建的 clip（控件手势回引用）。</summary>
    public Clip? LastAddedClip { get; private set; }

    /// <summary>文档被编辑/替换（含撤销重做）后通知自绘控件重画。</summary>
    public event Action? DocumentEdited;

    public IReadOnlyList<Pattern> Patterns => _session.Document.Patterns;

    public IReadOnlyList<Clip> PlaylistClips => _session.Document.Playlist;

    /// <summary>通道快照（导入/撤销后由 DocumentEdited 触发重取，ItemsControl 重建）。</summary>
    public IReadOnlyList<Channel> Channels => _session.Document.Channels.ToList();

    /// <summary>自动化目标列表：每通道 volume/pan + master volume（SPEC M2）。</summary>
    public IReadOnlyList<AutomationTargetItem> AutomationTargets
    {
        get
        {
            var targets = new List<AutomationTargetItem>();
            foreach (Channel channel in _session.Document.Channels)
            {
                string label = string.IsNullOrEmpty(channel.Name) ? $"ch{channel.Id}" : channel.Name;
                targets.Add(new AutomationTargetItem(
                    AutomationTarget.Channel(channel.Id), AutomationParameter.Volume, $"{label} · 音量"));
                targets.Add(new AutomationTargetItem(
                    AutomationTarget.Channel(channel.Id), AutomationParameter.Pan, $"{label} · 声像"));
                if (channel.Kind == ChannelKind.Plugin && _pluginCatalog is not null)
                {
                    foreach (PluginParameterInfo parameter in GetPluginParameters(channel))
                    {
                        if (!parameter.IsAutomatable)
                        {
                            continue;
                        }
                        targets.Add(new AutomationTargetItem(
                            AutomationTarget.PluginParameter(channel.Id, parameter.Id),
                            AutomationParameter.PluginParam,
                            $"{label} · {parameter.Name}", parameter.MinValue, parameter.MaxValue));
                    }
                }
            }
            targets.Add(new AutomationTargetItem(
                AutomationTarget.Master, AutomationParameter.Volume, "Master · 音量"));
            return targets;
        }
    }

    /// <summary>当前 lane 的点列表（目标缺失时为空表，控件安全降级）。</summary>
    public IReadOnlyList<AutomationPoint> CurrentAutomationLane
    {
        get
        {
            AutomationTargetItem? target = SelectedAutomationTarget;
            if (target is null || !AutomationLanes.Supports(target.Target, target.Parameter))
            {
                return [];
            }
            return AutomationLanes.Points(_session.Document, target.Target, target.Parameter);
        }
    }

    /// <summary>当前 lane 值域（volume [0,2]、pan [-1,1]、插件参数取描述值域）。</summary>
    public (double Min, double Max) CurrentAutomationRange => SelectedAutomationTarget switch
    {
        { Min: { } min, Max: { } max } => (min, max),
        { Parameter: AutomationParameter parameter } => AutomationLanes.Range(parameter),
        _ => (0, 1),
    };

    /// <summary>自动化手势拖动中的点 tick（null=无手势）；控件高亮用。</summary>
    public long? AutomationLiveTick => _autoGesture ? _autoLiveTick : null;

    /// <summary>播放头 tick（Mixer 只读推子显示值与 lane 播放线用）。</summary>
    public long PlayheadTick
    {
        get
        {
            (long num, long den) = TickMath.SamplesPerTick(_session.Document.SampleRate, _session.Document.Tempo);
            return num == 0 ? 0 : _playback.Position * den / num;
        }
    }

    /// <summary>Mixer 只读推子显示值：lane 有曲线时按播放头求值，否则静态值（SPEC M2）。</summary>
    public double GetAutomationDisplayValue(Channel channel, AutomationParameter parameter)
    {
        List<AutomationPoint> points = parameter == AutomationParameter.Volume
            ? channel.VolumePoints
            : channel.PanPoints;
        double fallback = parameter == AutomationParameter.Volume ? channel.Volume : channel.Pan;
        return AutomationMath.Evaluate(points, fallback, PlayheadTick);
    }

    public double GetMasterAutomationDisplayValue()
        => AutomationMath.Evaluate(
            _session.Document.MasterVolumePoints, _session.Document.MasterVolume, PlayheadTick);

    /// <summary>采样通道档位（Mixer 通道属性，SPEC M4 D6）：六档固定表。</summary>
    public IReadOnlyList<double> StretchSpeedSteps => AssetPreparer.StretchSpeedSteps;

    /// <summary>设置采样通道拉伸档位（命令入撤销栈）。</summary>
    public void SetChannelStretchSpeed(Channel channel, double speed)
    {
        if (channel.Kind != ChannelKind.Sampler)
        {
            return;
        }
        double target = AssetPreparer.NearestStretchStep(speed);
        if (Math.Abs(channel.StretchSpeed - target) < 1e-9)
        {
            return;
        }
        _session.Execute(new SetChannelStretchSpeedCommand(channel.Id, channel.StretchSpeed, target));
        AfterEdit();
    }

    /// <summary>开关切片演奏（命令入撤销栈；开启时强制档位 1.0）。</summary>
    public void SetChannelSliceMode(Channel channel, bool enabled)
    {
        if (channel.Kind != ChannelKind.Sampler || channel.SliceMode == enabled)
        {
            return;
        }
        _session.Execute(new SetChannelSliceModeCommand(channel.Id, channel.SliceMode, enabled));
        AfterEdit();
    }

    /// <summary>切片编辑面板当前通道（null=未选）。</summary>
    [ObservableProperty]
    private Channel? _selectedSliceChannel;

    /// <summary>采样通道（切片编辑面板与 Mixer 属性用）。</summary>
    public IReadOnlyList<Channel> SamplerChannels =>
        [.. _session.Document.Channels.Where(c => c.Kind == ChannelKind.Sampler)];

    /// <summary>资产帧数（波形面板横轴映射）。</summary>
    public long GetAssetFrameCount(string assetId)
        => _session.Document.Assets.FirstOrDefault(a => a.Id == assetId) is { } asset
            ? _session.GetAssetPeaks(assetId).Length * 256L
            : 0;

    /// <summary>资产波形峰值（切片编辑面板）。</summary>
    public float[] GetAssetPeaks(string assetId) => _session.GetAssetPeaks(assetId);

    /// <summary>资产的切片点（样本帧域）。</summary>
    public IReadOnlyList<long> GetSlicePoints(string assetId)
        => _session.Document.Assets.FirstOrDefault(a => a.Id == assetId)?.SlicePoints ?? [];

    /// <summary>切片点手势：拖动中每步以「当前帧 → 新帧」执行，事务合并为单条撤销。</summary>
    public void BeginSlicePointDrag()
    {
        if (_sliceGesture)
        {
            return;
        }
        _session.BeginTransaction("移动切片点");
        _sliceGesture = true;
    }

    public void DragSlicePoint(string assetId, long fromFrame, long toFrame)
    {
        if (!_sliceGesture)
        {
            return;
        }
        _session.Execute(new MoveSlicePointCommand(assetId, fromFrame, Math.Max(0, toFrame)));
    }

    public void EndSlicePointDrag()
    {
        if (!_sliceGesture)
        {
            return;
        }
        _sliceGesture = false;
        _session.EndTransaction();
        AfterEdit();
    }

    public void AddSlicePoint(string assetId, long frame)
    {
        _session.Execute(new AddSlicePointCommand(assetId, Math.Max(0, frame)));
        AfterEdit();
    }

    public void RemoveSlicePoint(string assetId, long frame)
    {
        _session.Execute(new RemoveSlicePointCommand(assetId, frame));
        AfterEdit();
    }

    /// <summary>插件参数显示值（Mixer 参数面板只读显示，竖切 4）：lane 有曲线按播放头求值。</summary>
    public double GetPluginParamDisplayValue(Channel channel, uint paramId, double fallback)
        => AutomationMath.Evaluate(channel.GetPluginParamPoints(paramId), fallback, PlayheadTick);

    /// <summary>master 是否带自动化曲线（Mixer 只读推子判定）。</summary>
    public bool HasMasterAutomation => _session.Document.MasterVolumePoints.Count > 0;

    public double MasterVolume
    {
        get => _session.Document.MasterVolume;
        set => SetMasterPreview(value);
    }

    private double _masterFrom;
    private bool _masterGesture;
    private bool _mixGesture;
    private Channel? _mixChannel;
    private double _mixFromVolume;
    private double _mixFromPan;

    private void SetMasterPreview(double value)
    {
        // 拖动中直接改文档实时反映播放；提交点（EndMasterEdit）走命令入撤销栈
        _session.Document.MasterVolume = Math.Clamp(value, 0, 2);
        if (_playback.IsPlaying)
        {
            _playback.UpdatePlan(CompileCurrent());
        }
    }

    public MainWindowViewModel(EditSession session, PlaybackService playback, IFileDialogService dialogs,
        RecoveryStore? recovery = null, PluginParameterCatalog? pluginCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(dialogs);
        _session = session;
        _playback = playback;
        _dialogs = dialogs;
        _recovery = recovery;
        _pluginCatalog = pluginCatalog;
        _tempo = session.Document.Tempo;
        RefreshTitle();
    }

    /// <summary>插件参数目录（SPEC M3 竖切 3）：Mixer 参数面板经此取描述（临时实例+缓存）。</summary>
    public PluginParameterCatalog? PluginCatalog => _pluginCatalog;

    public bool CanUndo => _session.History.CanUndo;

    public bool CanRedo => _session.History.CanRedo;

    [RelayCommand]
    private void Play()
    {
        _playback.RestorePluginStates(_session.PluginStates);
        if (IsSongMode)
        {
            _playback.Play(_session.Compile());
            IsPlaying = true;
            StatusText = "Song 播放中";
            return;
        }
        if (SelectedPattern is null)
        {
            StatusText = "没有可播放的 Pattern";
            return;
        }
        _playback.Play(_session.CompilePattern(SelectedPattern.Id));
        IsPlaying = true;
        StatusText = "Pattern 循环播放中";
    }

    [RelayCommand]
    private void Stop()
    {
        _playback.Stop();
        IsPlaying = false;
        StatusText = "已停止";
    }

    [RelayCommand]
    private void NewDemo()
    {
        Stop();
        _session.NewDemo();
        OnDocumentReplaced();
        StatusText = "演示工程已载入";
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        string? path = await _dialogs.OpenProjectAsync();
        if (path is null)
        {
            return;
        }
        Stop();
        _session.Open(path);
        OnDocumentReplaced();
        StatusText = $"已打开 {Path.GetFileName(path)}";
    }

    private void OnDocumentReplaced()
    {
        _playback.RestorePluginStates(_session.PluginStates);
        Tempo = _session.Document.Tempo;
        SelectedPattern = Patterns.FirstOrDefault();
        RefreshAutomationTargets();
        OnPropertyChanged(nameof(Patterns));
        OnPropertyChanged(nameof(PlaylistClips));
        OnPropertyChanged(nameof(Channels));
        OnPropertyChanged(nameof(MasterVolume));
        RefreshTitle();
        NotifyHistoryChanged();
        DocumentEdited?.Invoke();
    }

    /// <summary>通道增删后重建目标列表；选中项对应通道消失时回到首项。</summary>
    private void RefreshAutomationTargets()
    {
        AutomationTargetItem? current = SelectedAutomationTarget;
        if (current is not null
            && current.Target.IsMaster)
        {
            return; // master 永远有效
        }
        if (current is not null
            && _session.Document.Channels.Any(c => c.Id == current.Target.ChannelId))
        {
            OnPropertyChanged(nameof(AutomationTargets));
            return; // 通道仍在：列表刷新但选中保持
        }
        IReadOnlyList<AutomationTargetItem> targets = AutomationTargets;
        if (current is null || !targets.Any(t => t.Target == current.Target && t.Parameter == current.Parameter))
        {
            SelectedAutomationTarget = targets.FirstOrDefault();
        }
        OnPropertyChanged(nameof(AutomationTargets));
    }

    partial void OnSelectedPatternChanged(Pattern? value)
    {
        if (_playback.IsPlaying)
        {
            _playback.UpdatePlan(CompileCurrent());
        }
    }

    partial void OnSelectedAutomationTargetChanged(AutomationTargetItem? value)
    {
        EndAutomationGesture();
        OnPropertyChanged(nameof(CurrentAutomationLane));
        OnPropertyChanged(nameof(CurrentAutomationRange));
    }

    partial void OnPendingRecoveryChanged(RecoverySnapshot? value)
        => OnPropertyChanged(nameof(HasPendingRecovery));

    /// <summary>启动检测：恢复区存在快照（显式保存即清理，存在即代表未保存编辑）则提示恢复/丢弃。</summary>
    public void CheckRecoveryOnStartup()
    {
        if (_recovery is null)
        {
            return;
        }
        PendingRecovery = _recovery.FindAll().FirstOrDefault();
    }

    /// <summary>恢复：从快照装载，ProjectPath 记原路径、保持脏标记；快照保留至下次保存（防二次崩溃）。</summary>
    [RelayCommand]
    private void RestoreRecovery()
    {
        RecoverySnapshot? snapshot = PendingRecovery;
        if (snapshot is null)
        {
            return;
        }
        try
        {
            Stop();
            _session.Open(snapshot.SnapshotPath, projectPathOverride: snapshot.SourceProjectPath);
            OnDocumentReplaced();
            PendingRecovery = null;
            StatusText = snapshot.SourceProjectPath is null
                ? "已恢复未保存的自动快照"
                : $"已恢复 {Path.GetFileName(snapshot.SourceProjectPath)} 的自动快照";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            StatusText = "恢复失败：快照已损坏";
            PendingRecovery = null;
            _recovery?.Remove(snapshot);
        }
    }

    /// <summary>丢弃：删除该快照，继续以当前工程启动。</summary>
    [RelayCommand]
    private void DiscardRecovery()
    {
        RecoverySnapshot? snapshot = PendingRecovery;
        if (snapshot is null)
        {
            return;
        }
        _recovery?.Remove(snapshot);
        PendingRecovery = null;
        StatusText = "已丢弃自动快照";
    }

    /// <summary>View 定时器（1s）驱动：满足策略（空闲 ≥5s 或 ≥50 次编辑）则写恢复快照。</summary>
    public void AutosaveTick()
    {
        if (_recovery is null || !AutosavePolicy.ShouldSnapshot(_session, DateTime.UtcNow))
        {
            return;
        }
        try
        {
            RecoverySnapshot snapshot = _recovery.Save(_session);
            StatusText = $"已自动保存恢复快照（{snapshot.SavedAtUtc:HH:mm:ss}）";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // 恢复区不可写不阻断编辑，等下一 tick 重试
        }
    }

    /// <summary>正常退出清理全部恢复项；崩溃无此调用，快照存活供下次启动恢复。</summary>
    public void ClearRecoveryOnExit() => _recovery?.ClearAll();

    partial void OnIsSongModeChanged(bool value)
    {
        if (_playback.IsPlaying)
        {
            _playback.UpdatePlan(CompileCurrent());
            StatusText = value ? "Song 播放中" : "Pattern 循环播放中";
        }
    }

    private RenderPlan CompileCurrent()
    {
        if (IsSongMode || SelectedPattern is null)
        {
            return _session.Compile();
        }
        return _session.CompilePattern(SelectedPattern.Id);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        string? path = _session.ProjectPath ?? await _dialogs.SaveProjectAsync();
        if (path is null)
        {
            return;
        }
        if (_playback.HasPluginHost)
        {
            // 保存前抓取实例 state（含插件内部随机种子/滤波状态，SPEC M3 竖切 3）
            _session.PluginStates = _playback.CapturePluginStates(CompileCurrent());
        }
        _session.Save(path);
        _recovery?.RemoveStale(path);
        RefreshTitle();
        StatusText = $"已保存 {Path.GetFileName(path)}";
    }

    [RelayCommand]
    private void Undo()
    {
        _session.Undo();
        AfterEdit();
    }

    [RelayCommand]
    private void Redo()
    {
        _session.Redo();
        AfterEdit();
    }

    /// <summary>Piano Roll 手势（按下→拖动→释放经事务合并为单条撤销）：命令执行全部在 VM，控件只发意图。</summary>
    public void BeginAddNote(long tick, int key)
    {
        if (SelectedPattern is null || _session.Document.Channels.Count == 0)
        {
            return;
        }
        _session.BeginTransaction("添加音符");
        var note = new Note
        {
            ChannelId = _session.Document.Channels[0].Id,
            Tick = tick,
            Key = key,
            Velocity = 1.0,
            LengthTicks = TimeBase.Ppq,
        };
        _session.Execute(new AddNoteCommand(SelectedPattern.Id, note));
        _gesture = NoteGesture.AddResize;
        _gestureNote = note;
        LastAddedNote = note;
        _gestureFromTick = tick;
        _gestureFromKey = key;
        _gestureFromLength = note.LengthTicks;
    }

    public void BeginMoveNote(Note note)
    {
        if (SelectedPattern is null)
        {
            return;
        }
        _session.BeginTransaction("移动音符");
        _gesture = NoteGesture.Move;
        _gestureNote = note;
        _gestureFromTick = note.Tick;
        _gestureFromKey = note.Key;
        _gestureFromLength = note.LengthTicks;
    }

    public void BeginResizeNote(Note note)
    {
        if (SelectedPattern is null)
        {
            return;
        }
        _session.BeginTransaction("调整音符长度");
        _gesture = NoteGesture.Resize;
        _gestureNote = note;
        _gestureFromTick = note.Tick;
        _gestureFromKey = note.Key;
        _gestureFromLength = note.LengthTicks;
    }

    public void DragNoteMove(Note note, long toTick, int toKey)
    {
        if (_gesture != NoteGesture.Move || SelectedPattern is null)
        {
            return;
        }
        _session.Execute(new MoveNoteCommand(
            SelectedPattern.Id, note, _gestureFromTick, _gestureFromKey, toTick, toKey));
    }

    public void DragNoteLength(Note note, long toLengthTicks)
    {
        if ((_gesture != NoteGesture.Resize && _gesture != NoteGesture.AddResize) || SelectedPattern is null)
        {
            return;
        }
        _session.Execute(new ResizeNoteCommand(
            SelectedPattern.Id, note, _gestureFromLength, Math.Max(1, toLengthTicks)));
    }

    public void RemoveNote(Note note)
    {
        if (SelectedPattern is null)
        {
            return;
        }
        _session.Execute(new RemoveNoteCommand(SelectedPattern.Id, note));
        AfterEdit();
    }

    /// <summary>手势结束：事务入栈 + 重编译交换（停止-交换-恢复）。</summary>
    public void EndNoteGesture()
    {
        if (_gesture == NoteGesture.None)
        {
            return;
        }
        _gesture = NoteGesture.None;
        _gestureNote = null;
        _session.EndTransaction();
        AfterEdit();
    }

    /// <summary>由 View 定时器轮询：同步播放位置与状态；循环模式不到末尾停止，Song 模式到末尾自动停止。</summary>
    public void UpdatePosition()
    {
        IsPlaying = _playback.IsPlaying;
        OnPropertyChanged(nameof(PlayheadTick));
        if (!_playback.IsPlaying)
        {
            return;
        }
        long position = _playback.Position;
        PositionText = FormatTime(position, _session.Document.SampleRate);
        if (!_playback.IsLooping && _playback.TotalSamples > 0 && position >= _playback.TotalSamples)
        {
            Stop();
            PositionText = FormatTime(_playback.TotalSamples, _session.Document.SampleRate);
        }
    }

    public static string FormatTime(long samples, int sampleRate)
    {
        if (sampleRate <= 0)
        {
            return "0:00.000";
        }
        double seconds = (double)samples / sampleRate;
        int minutes = (int)(seconds / 60);
        double rest = seconds - minutes * 60;
        return $"{minutes}:{rest.ToString("00.000", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Playlist clip 手势：放置/删除即时；拖拽经事务合并为单条撤销。</summary>
    public void AddClipAt(Pattern pattern, long tick)
    {
        var clip = new Clip { PatternId = pattern.Id, StartTick = Math.Max(0, tick) };
        _session.Execute(new AddClipCommand(clip));
        LastAddedClip = clip;
        AfterEdit();
    }

    public void RemoveClip(Clip clip)
    {
        _session.Execute(new RemoveClipCommand(clip));
        AfterEdit();
    }

    public void BeginMoveClip(Clip clip)
    {
        _session.BeginTransaction("移动 clip");
        _clipGesture = true;
        _clipFromTick = clip.StartTick;
        _clipFromPatternId = clip.PatternId;
    }

    public void DragClip(Clip clip, long toTick, Pattern toPattern)
    {
        if (!_clipGesture)
        {
            return;
        }
        _session.Execute(new MoveClipCommand(
            clip, _clipFromTick, _clipFromPatternId, Math.Max(0, toTick), toPattern.Id));
    }

    public void EndClipGesture()
    {
        if (!_clipGesture)
        {
            return;
        }
        _clipGesture = false;
        _session.EndTransaction();
        AfterEdit();
    }

    /// <summary>自动化手势（SPEC M2：画/拖/删三件套，事务合并单步撤销；拖动中不重编译——落点才交换 plan）。</summary>
    public void BeginAddAutomationPoint(long tick, double value)
    {
        if (!BeginAutomationGesture("添加自动化点"))
        {
            return;
        }
        _session.Execute(new AddAutomationPointCommand(
            SelectedAutomationTarget!.Target, SelectedAutomationTarget.Parameter,
            new AutomationPoint { Tick = Math.Max(0, tick), Value = value }));
        _autoLiveTick = Math.Max(0, tick);
    }

    public void BeginDragAutomationPoint(long tick)
    {
        if (!BeginAutomationGesture("拖动自动化点"))
        {
            return;
        }
        _autoLiveTick = tick;
    }

    public void DragAutomationPoint(long newTick, double newValue)
    {
        if (!_autoGesture || SelectedAutomationTarget is null)
        {
            return;
        }
        newTick = Math.Max(0, newTick);
        _session.Execute(new MoveAutomationPointCommand(
            SelectedAutomationTarget.Target, SelectedAutomationTarget.Parameter,
            currentTick: _autoLiveTick, newTick, newValue));
        _autoLiveTick = newTick;
    }

    public void RemoveAutomationPoint(long tick)
    {
        if (SelectedAutomationTarget is null)
        {
            return;
        }
        _session.Execute(new RemoveAutomationPointCommand(
            SelectedAutomationTarget.Target, SelectedAutomationTarget.Parameter, tick));
        AfterEdit();
    }

    public void EndAutomationGesture()
    {
        if (!_autoGesture)
        {
            return;
        }
        _autoGesture = false;
        _session.EndTransaction();
        AfterEdit();
    }

    private bool BeginAutomationGesture(string title)
    {
        if (_autoGesture || SelectedAutomationTarget is null)
        {
            return false;
        }
        _session.BeginTransaction(title);
        _autoGesture = true;
        return true;
    }

    [RelayCommand]
    private async Task ImportSampleAsync()
    {
        string? path = await _dialogs.OpenWavAsync();
        if (path is null)
        {
            return;
        }
        try
        {
            Channel channel = _session.ImportSample(path);
            AfterEdit();
            StatusText = $"已导入采样 → 通道「{channel.Name}」";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            StatusText = $"导入失败：{Path.GetFileName(path)} 不是可解码的 WAV";
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        string? path = await _dialogs.SaveWavAsync();
        if (path is null)
        {
            return;
        }
        try
        {
            long frames = _session.ExportWav(path);
            StatusText = $"已导出 {Path.GetFileName(path)}（{frames / (double)_session.Document.SampleRate:0.###}s）";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            StatusText = $"导出失败：{ex.Message}";
        }
    }

    /// <summary>Mixer 推子手势：拖动中直接改文档实时反映，释放时一条命令入撤销栈。</summary>
    public void BeginMixEdit(Channel channel)
    {
        _mixGesture = true;
        _mixChannel = channel;
        _mixFromVolume = channel.Volume;
        _mixFromPan = channel.Pan;
    }

    public void DragMixEdit(Channel channel, double volume, double pan)
    {
        if (!_mixGesture || !ReferenceEquals(channel, _mixChannel))
        {
            return;
        }
        channel.Volume = Math.Clamp(volume, 0, 2);
        channel.Pan = Math.Clamp(pan, -1, 1);
        if (_playback.IsPlaying)
        {
            _playback.UpdatePlan(CompileCurrent());
        }
    }

    public void EndMixEdit(Channel channel)
    {
        if (!_mixGesture || !ReferenceEquals(channel, _mixChannel))
        {
            return;
        }
        _mixGesture = false;
        _mixChannel = null;
        if (channel.Volume != _mixFromVolume || channel.Pan != _mixFromPan)
        {
            _session.Execute(new SetChannelMixCommand(
                channel.Id, _mixFromVolume, _mixFromPan, channel.Volume, channel.Pan));
        }
        AfterEdit();
    }

    /// <summary>插件参数手势（SPEC M3 竖切 3）：拖动中直接改文档+换 plan 实时反映，释放一条命令入撤销栈。</summary>
    public void BeginPluginParamEdit(Channel channel, uint parameterId)
    {
        _pluginParamGesture = true;
        _pluginParamChannel = channel;
        _pluginParamId = parameterId;
        PluginParameterValue? existing = channel.PluginParameters.FirstOrDefault(p => p.Id == parameterId);
        _pluginParamFrom = existing?.Value
            ?? _pluginCatalog?.Get(channel, _session.Document)
                .FirstOrDefault(p => p.Id == parameterId)?.DefaultValue
            ?? 0;
        _session.BeginTransaction("调整插件参数");
    }

    public void DragPluginParam(Channel channel, uint parameterId, double value)
    {
        if (!_pluginParamGesture || !ReferenceEquals(channel, _pluginParamChannel) || parameterId != _pluginParamId)
        {
            return;
        }
        _session.Execute(new SetPluginParameterCommand(channel.Id, parameterId, _pluginParamFrom, value));
        if (_playback.IsPlaying)
        {
            _playback.UpdatePlan(CompileCurrent());
        }
    }

    public void EndPluginParamEdit()
    {
        if (!_pluginParamGesture)
        {
            return;
        }
        _pluginParamGesture = false;
        _pluginParamChannel = null;
        _session.EndTransaction();
        AfterEdit();
    }

    /// <summary>插件通道参数描述（Mixer 面板用；无目录或非插件通道返回空）。</summary>
    public IReadOnlyList<PluginParameterInfo> GetPluginParameters(Channel channel)
    {
        try
        {
            return _pluginCatalog?.Get(channel, _session.Document) ?? [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    public void BeginMasterEdit()
    {
        _masterGesture = true;
        _masterFrom = _session.Document.MasterVolume;
    }

    public void EndMasterEdit()
    {
        if (!_masterGesture)
        {
            return;
        }
        _masterGesture = false;
        if (_session.Document.MasterVolume != _masterFrom)
        {
            _session.Execute(new SetMasterVolumeCommand(_masterFrom, _session.Document.MasterVolume));
        }
        AfterEdit();
    }

    partial void OnSelectedSliceChannelChanged(Channel? value)
    {
        SliceEditorRefresh?.Invoke();
    }

    /// <summary>切片编辑面板刷新回调（由宿主挂载，控件重取波形与切片点）。</summary>
    public Action? SliceEditorRefresh { get; set; }

    private void AfterEdit()
    {
        RefreshTitle();
        NotifyHistoryChanged();
        OnPropertyChanged(nameof(Channels));
        OnPropertyChanged(nameof(SamplerChannels));
        OnPropertyChanged(nameof(MasterVolume));
        OnPropertyChanged(nameof(CurrentAutomationLane));
        OnPropertyChanged(nameof(PlayheadTick));
        RefreshAutomationTargets();
        DocumentEdited?.Invoke();
        if (_playback.IsPlaying)
        {
            _playback.UpdatePlan(CompileCurrent());
        }
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void RefreshTitle()
    {
        string name = _session.ProjectPath is null
            ? "未命名"
            : Path.GetFileNameWithoutExtension(_session.ProjectPath);
        Title = $"{name}{(_session.IsDirty ? "*" : "")} — Weftone";
    }
}