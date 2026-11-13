using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Weftone.App.ViewModels;

namespace Weftone.App;

public partial class MainWindow : Window
{
    private DispatcherTimer? _positionTimer;
    private MainWindowViewModel? _subscribed;

    public MainWindow()
    {
        InitializeComponent();
        RollScroll.ScrollChanged += (_, _) => SyncRollViewport();
        RollScroll.SizeChanged += (_, _) => SyncRollViewport();
        PlaylistScroll.ScrollChanged += (_, _) => SyncPlaylistViewport();
        PlaylistScroll.SizeChanged += (_, _) => SyncPlaylistViewport();
        DataContextChanged += (_, _) => SubscribeViewModel();
    }

    /// <summary>滚动/尺寸变化时同步视口：Roll 用于裁剪，Keys 侧栏纵向跟随，自动化 lane 横向跟随。</summary>
    private void SyncRollViewport()
    {
        Roll.SetViewport(RollScroll.Offset.X, RollScroll.Offset.Y,
            RollScroll.Viewport.Width, RollScroll.Viewport.Height);
        Keys.VerticalOffset = RollScroll.Offset.Y;
        LaneScroll.Offset = new Vector(RollScroll.Offset.X, 0);
        Lane.SetViewport(RollScroll.Offset.X, RollScroll.Viewport.Width);
    }

    private void SyncPlaylistViewport()
    {
        Playlist.SetViewport(PlaylistScroll.Viewport.Width);
    }

    /// <summary>撤销/重做/换工程后通知自绘控件重画（文档变更无集合通知）。</summary>
    private void SubscribeViewModel()
    {
        if (_subscribed is not null)
        {
            _subscribed.DocumentEdited -= OnDocumentEdited;
        }
        _subscribed = DataContext as MainWindowViewModel;
        if (_subscribed is not null)
        {
            _subscribed.DocumentEdited += OnDocumentEdited;
        }
    }

    private void OnDocumentEdited()
    {
        Roll.InvalidateVisual();
        Playlist.InvalidateVisual();
        Lane.InvalidateVisual();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
        {
            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _positionTimer.Tick += (_, _) => vm.UpdatePosition();
            _positionTimer.Start();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _positionTimer?.Stop();
        base.OnClosed(e);
    }
}