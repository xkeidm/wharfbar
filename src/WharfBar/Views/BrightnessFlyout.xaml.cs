using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class BrightnessFlyout : UserControl
{
    private MonitorBrightnessService? _service;
    private bool _isUpdatingUi;

    public BrightnessFlyout()
    {
        InitializeComponent();
    }

    internal void Attach(MonitorBrightnessService service)
    {
        if (_service is not null)
        {
            _service.MonitorsChanged -= OnMonitorsChanged;
        }

        _service = service;
        if (_service is not null)
        {
            _service.MonitorsChanged += OnMonitorsChanged;
            RebuildMonitorList();
        }
    }

    private void OnMonitorsChanged(object? sender, EventArgs e)
    {
        RebuildMonitorList();
    }

    internal void Refresh()
    {
        _service?.RefreshMonitors();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void RebuildMonitorList()
    {
        if (_service is null)
        {
            return;
        }

        _isUpdatingUi = true;
        try
        {
            MonitorsListPanel.Children.Clear();
            var monitors = _service.Monitors;

            if (monitors.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "Bağlı monitör bulunamadı",
                    FontSize = 12,
                    Opacity = 0.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 16)
                };
                MonitorsListPanel.Children.Add(emptyText);
                MasterPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // Calculate average brightness for master slider
            var supportedMonitors = monitors.Where(m => m.IsSupported).ToList();
            MasterPanel.Visibility = supportedMonitors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (supportedMonitors.Count > 0)
            {
                int avgBrightness = (int)Math.Round(supportedMonitors.Average(m => m.CurrentBrightness));
                MasterBrightnessSlider.Value = avgBrightness;
                MasterPercentText.Text = $"{avgBrightness}%";
            }

            foreach (var monitor in monitors)
            {
                MonitorsListPanel.Children.Add(CreateMonitorCard(monitor));
            }
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private UIElement CreateMonitorCard(MonitorItem monitor)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("ControlFillColorDefaultBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            SnapsToDevicePixels = true
        };

        var stack = new StackPanel();

        // Header Row: Icon + Name + Percentage
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        var percentText = new TextBlock
        {
            Text = monitor.IsSupported ? $"{monitor.CurrentBrightness}%" : "N/A",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(percentText, Dock.Right);
        dock.Children.Add(percentText);

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        var iconText = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("AppIconFontFamily"),
            FontSize = 14,
            Text = monitor.IsWmi ? "\uE7F4" : "\uE7F4", // Segoe MDL2 monitor glyph
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(iconText);

        var nameText = new TextBlock
        {
            Text = monitor.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200
        };
        titleStack.Children.Add(nameText);

        dock.Children.Add(titleStack);
        stack.Children.Add(dock);

        // Control Row: Slider or Unsupported Notice
        if (monitor.IsSupported)
        {
            var slider = new Slider
            {
                Minimum = monitor.MinBrightness,
                Maximum = monitor.MaxBrightness,
                Value = monitor.CurrentBrightness,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = true,
                Tag = monitor
            };

            slider.ValueChanged += (s, e) =>
            {
                int val = (int)e.NewValue;
                percentText.Text = $"{val}%";

                if (!_isUpdatingUi && _service is not null)
                {
                    _service.SetBrightness(monitor, val);
                }
            };

            stack.Children.Add(slider);
        }
        else
        {
            var noticeText = new TextBlock
            {
                Text = "DDC/CI / WMI parlaklık kontrolü desteklenmiyor",
                FontSize = 11,
                Opacity = 0.5,
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(noticeText);
        }

        border.Child = stack;
        return border;
    }

    private void OnMasterSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)e.NewValue;
        MasterPercentText.Text = $"{val}%";

        if (!_isUpdatingUi && _service is not null)
        {
            _service.SetAllBrightness(val);
            RebuildMonitorList();
        }
    }
}
