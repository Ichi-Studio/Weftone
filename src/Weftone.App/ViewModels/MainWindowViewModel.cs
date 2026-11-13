using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private readonly EditSession _session;
    private readonly PlaybackService _playback;
    private readonly IFileDialogService _dialogs;
    private NoteGesture _gesture;
    private Note? _gestureNote;
    private long _gestureFromTick;
    private long _gestureFromLength;
    private int _gestureFromKey;

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

    /// <summary>最近一次 BeginAddNote 创建的音符（控件手势回引用）。</summary>
    public Note? LastAddedNote { get; private set; }

    public IReadOnlyList<Pattern> Patterns => _session.Document.Patterns;

    public MainWindowViewModel(EditSession session, PlaybackService playback, IFileDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(dialogs);
        _session = session;
        _playback = playback;
        _dialogs = dialogs;
        _tempo = session.Document.Tempo;
        RefreshTitle();
    }

    public bool CanUndo => _session.History.CanUndo;

    public bool CanRedo => _session.History.CanRedo;

    [RelayCommand]
    private void Play()
    {
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
        Tempo = _session.Document.Tempo;
        SelectedPattern = Patterns.FirstOrDefault();
        OnPropertyChanged(nameof(Patterns));
        RefreshTitle();
        NotifyHistoryChanged();
    }

    partial void OnSelectedPatternChanged(Pattern? value)
    {
        if (_playback.IsPlaying)
        {
            _playback.UpdatePlan(CompileCurrent());
        }
    }

    private RenderPlan CompileCurrent() =>
        SelectedPattern is null ? _session.Compile() : _session.CompilePattern(SelectedPattern.Id);

    [RelayCommand]
    private async Task SaveAsync()
    {
        string? path = _session.ProjectPath ?? await _dialogs.SaveProjectAsync();
        if (path is null)
        {
            return;
        }
        _session.Save(path);
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

    private void AfterEdit()
    {
        RefreshTitle();
        NotifyHistoryChanged();
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