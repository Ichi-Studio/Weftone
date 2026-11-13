using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weftone.Abstractions.Rendering;
using Weftone.App.Services;
using Weftone.Application;

namespace Weftone.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly EditSession _session;
    private readonly PlaybackService _playback;
    private readonly IFileDialogService _dialogs;

    [ObservableProperty]
    private string _title = "Weftone";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _positionText = "0:00.000";

    [ObservableProperty]
    private int _tempo;

    [ObservableProperty]
    private string _statusText = "就绪";

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
        RenderPlan plan = _session.Compile();
        _playback.Play(plan);
        IsPlaying = true;
        StatusText = "播放中";
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
        Tempo = _session.Document.Tempo;
        RefreshTitle();
        NotifyHistoryChanged();
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
        Tempo = _session.Document.Tempo;
        RefreshTitle();
        NotifyHistoryChanged();
        StatusText = $"已打开 {Path.GetFileName(path)}";
    }

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

    /// <summary>由 View 定时器轮询：同步播放位置与状态，Song 模式到末尾自动停止。</summary>
    public void UpdatePosition()
    {
        IsPlaying = _playback.IsPlaying;
        if (!_playback.IsPlaying)
        {
            return;
        }
        long position = _playback.Position;
        PositionText = FormatTime(position, _session.Document.SampleRate);
        if (position >= _playback.TotalSamples)
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
            _playback.UpdatePlan(_session.Compile());
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