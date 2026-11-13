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