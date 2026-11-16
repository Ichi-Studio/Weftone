using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Weftone.Abstractions.Plugins;
using Weftone.App.ViewModels;
using Weftone.Domain;

namespace Weftone.App.Controls;

/// <summary>
/// Mixer 面板（M1b 竖切 4）：每通道竖推子（音量 0..2）+ 横推子（pan -1..1）+ 名称，
/// 右端 master 推子。拖动中经 VM 预览实时反映播放，释放时一条命令入撤销栈
/// （spec §自绘：Mixer 用标准控件组合）。
/// </summary>
public sealed class MixerPanel : ContentControl
{
    public static readonly StyledProperty<MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<MixerPanel, MainWindowViewModel?>(nameof(ViewModel));

    private MainWindowViewModel? _subscribed;
    private readonly List<Action> _autoValueUpdaters = [];

    static MixerPanel()
    {
        ViewModelProperty.Changed.AddClassHandler<MixerPanel>(static (c, _) => c.OnViewModelChanged());
    }

    public MainWindowViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void OnViewModelChanged()
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }
        Rebuild();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.Channels) or nameof(MainWindowViewModel.MasterVolume))
        {
            Rebuild();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.PlayheadTick))
        {
            // 有曲线的推子只读显示当前播放值（SPEC M2：不做写曲线的推子录制）
            foreach (Action updater in _autoValueUpdaters)
            {
                updater();
            }
        }
    }

    private void Rebuild()
    {
        _autoValueUpdaters.Clear();
        var stripPanel = new StackPanel { Orientation = Orientation.Horizontal };
        if (ViewModel is not null)
        {
            foreach (Channel channel in ViewModel.Channels)
            {
                stripPanel.Children.Add(BuildChannelStrip(channel));
            }
            stripPanel.Children.Add(BuildMasterStrip());
        }
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stripPanel,
        };
    }

    private static Border StripShell(bool isMaster, params Control[] content)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        foreach (Control control in content)
        {
            panel.Children.Add(control);
        }
        return new Border
        {
            Width = 92,
            Padding = new Thickness(6, 8),
            Margin = new Thickness(1, 0),
            Background = new SolidColorBrush(isMaster
                ? Color.FromRgb(0x2A, 0x22, 0x30)
                : Color.FromRgb(0x22, 0x22, 0x28)),
            Child = panel,
        };
    }

    private Control BuildChannelStrip(Channel channel)
    {
        MainWindowViewModel? vm = ViewModel;
        var name = new TextBlock
        {
            Text = channel.Name,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        bool autoVolume = channel.VolumePoints.Count > 0;
        var volume = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 2,
            Value = autoVolume && vm is not null
                ? vm.GetAutomationDisplayValue(channel, AutomationParameter.Volume)
                : channel.Volume,
            Height = 110,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsEnabled = !autoVolume,
        };
        var volumeLabel = new TextBlock
        {
            Text = autoVolume
                ? $"A {volume.Value:0.00}"
                : channel.Volume.ToString("0.00"),
            Foreground = new SolidColorBrush(autoVolume
                ? Color.FromRgb(0x8A, 0xF0, 0xE0)
                : Color.FromRgb(0xA8, 0xA8, 0xB0)),
            TextAlignment = TextAlignment.Center,
        };
        bool autoPan = channel.PanPoints.Count > 0;
        var pan = new Slider
        {
            Orientation = Orientation.Horizontal,
            Minimum = -1,
            Maximum = 1,
            Value = autoPan && vm is not null
                ? vm.GetAutomationDisplayValue(channel, AutomationParameter.Pan)
                : channel.Pan,
            Width = 76,
            IsEnabled = !autoPan,
        };
        var paramPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };

        if (vm is not null)
        {
            if (autoVolume)
            {
                _autoValueUpdaters.Add(() =>
                {
                    double value = vm.GetAutomationDisplayValue(channel, AutomationParameter.Volume);
                    volume.Value = value;
                    volumeLabel.Text = $"A {value:0.00}";
                });
            }
            if (autoPan)
            {
                _autoValueUpdaters.Add(() => pan.Value = vm.GetAutomationDisplayValue(channel, AutomationParameter.Pan));
            }
        }

        if (vm is not null && channel.Kind == ChannelKind.Plugin)
        {
            // 插件参数面板（SPEC M3 竖切 3）：描述经 PluginParameterCatalog（临时实例+缓存），
            // 值走文档（SetPluginParameterCommand 事务撤销）；最多显示 4 个防溢出
            foreach (PluginParameterInfo parameter in vm.GetPluginParameters(channel).Take(4))
            {
                PluginParameterValue? stored = channel.PluginParameters.FirstOrDefault(p => p.Id == parameter.Id);
                double current = stored?.Value ?? parameter.DefaultValue;
                var slider = new Slider
                {
                    Orientation = Orientation.Horizontal,
                    Minimum = parameter.MinValue,
                    Maximum = parameter.MaxValue,
                    Value = current,
                    Width = 76,
                };
                var label = new TextBlock
                {
                    Text = $"{parameter.Name} {current:0.###}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0xF0, 0xE0)),
                    TextAlignment = TextAlignment.Center,
                };
                bool dragging = false;
                slider.AddHandler(PointerPressedEvent, (_, _) =>
                {
                    dragging = true;
                    vm.BeginPluginParamEdit(channel, parameter.Id);
                }, RoutingStrategies.Tunnel);
                slider.AddHandler(PointerReleasedEvent, (_, _) =>
                {
                    if (dragging)
                    {
                        dragging = false;
                        vm.EndPluginParamEdit();
                    }
                }, RoutingStrategies.Tunnel);
                slider.PropertyChanged += (_, e) =>
                {
                    if (e.Property == Slider.ValueProperty && dragging)
                    {
                        label.Text = $"{parameter.Name} {slider.Value:0.###}";
                        vm.DragPluginParam(channel, parameter.Id, slider.Value);
                    }
                };
                paramPanel.Children.Add(slider);
                paramPanel.Children.Add(label);
            }
        }

        var drag = new MixDragState();
        volume.AddHandler(PointerPressedEvent, (_, _) =>
        {
            drag.Volume = true;
            vm?.BeginMixEdit(channel);
        }, RoutingStrategies.Tunnel);
        volume.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            if (drag.Volume)
            {
                drag.Volume = false;
                vm?.EndMixEdit(channel);
            }
        }, RoutingStrategies.Tunnel);
        volume.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && drag.Volume)
            {
                volumeLabel.Text = volume.Value.ToString("0.00");
                vm?.DragMixEdit(channel, volume.Value, channel.Pan);
            }
        };

        pan.AddHandler(PointerPressedEvent, (_, _) =>
        {
            drag.Pan = true;
            vm?.BeginMixEdit(channel);
        }, RoutingStrategies.Tunnel);
        pan.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            if (drag.Pan)
            {
                drag.Pan = false;
                vm?.EndMixEdit(channel);
            }
        }, RoutingStrategies.Tunnel);
        pan.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && drag.Pan)
            {
                vm?.DragMixEdit(channel, channel.Volume, pan.Value);
            }
        };
        return StripShell(isMaster: false, name, volume, volumeLabel, pan, paramPanel);
    }

    private Control BuildMasterStrip()
    {
        MainWindowViewModel? vm = ViewModel;
        bool autoMaster = vm?.HasMasterAutomation == true;
        double master = autoMaster && vm is not null
            ? vm.GetMasterAutomationDisplayValue()
            : vm?.MasterVolume ?? 1;
        var title = new TextBlock
        {
            Text = "Master",
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
        };
        var volume = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 2,
            Value = master,
            Height = 110,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsEnabled = !autoMaster,
        };
        var label = new TextBlock
        {
            Text = autoMaster ? $"A {master:0.00}" : master.ToString("0.00"),
            Foreground = new SolidColorBrush(autoMaster
                ? Color.FromRgb(0x8A, 0xF0, 0xE0)
                : Color.FromRgb(0xE8, 0xC8, 0xFF)),
            TextAlignment = TextAlignment.Center,
        };
        if (vm is not null && autoMaster)
        {
            _autoValueUpdaters.Add(() =>
            {
                double value = vm.GetMasterAutomationDisplayValue();
                volume.Value = value;
                label.Text = $"A {value:0.00}";
            });
        }
        bool dragging = false;
        volume.AddHandler(PointerPressedEvent, (_, _) =>
        {
            dragging = true;
            vm?.BeginMasterEdit();
        }, RoutingStrategies.Tunnel);
        volume.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            if (dragging)
            {
                dragging = false;
                vm?.EndMasterEdit();
            }
        }, RoutingStrategies.Tunnel);
        volume.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && dragging && vm is not null)
            {
                label.Text = volume.Value.ToString("0.00");
                vm.MasterVolume = volume.Value;
            }
        };
        return StripShell(isMaster: true, title, volume, label);
    }

    private sealed class MixDragState
    {
        public bool Volume;
        public bool Pan;
    }
}
