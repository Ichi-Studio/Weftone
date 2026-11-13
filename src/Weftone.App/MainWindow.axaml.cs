using Avalonia.Controls;
using Avalonia.Threading;
using Weftone.App.ViewModels;

namespace Weftone.App;

public partial class MainWindow : Window
{
    private DispatcherTimer? _positionTimer;

    public MainWindow()
    {
        InitializeComponent();
        RollScroll.ScrollChanged += (_, _) => SyncRollViewport();
        RollScroll.SizeChanged += (_, _) => SyncRollViewport();
    }

    /// <summary>滚动/尺寸变化时同步视口：Roll 用于裁剪，Keys 侧栏纵向跟随。</summary>
    private void SyncRollViewport()
    {
        Roll.SetViewport(RollScroll.Offset.X, RollScroll.Offset.Y,
            RollScroll.Viewport.Width, RollScroll.Viewport.Height);
        Keys.VerticalOffset = RollScroll.Offset.Y;
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