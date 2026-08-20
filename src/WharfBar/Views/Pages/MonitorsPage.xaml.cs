using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

public partial class MonitorsPage : Page
{
    private bool _isLoading = true;

    public MonitorsPage()
    {
        InitializeComponent();
        RefreshList();
        Loaded += OnFirstLoaded;
    }

    private App WharfApp => (App)Application.Current;
    private MonitorManager? Monitors => WharfApp.Monitors;

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        _isLoading = false;
    }

    private sealed class Row
    {
        public string DeviceId { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public Visibility PrimaryBadgeVisibility { get; init; } = Visibility.Collapsed;
        public bool TopBar { get; set; }
        public bool Dock { get; set; }
    }

    private void RefreshList()
    {
        if (Monitors is null)
        {
            return;
        }

        var rows = new List<Row>();
        foreach (MonitorInfo monitor in Monitors.CurrentMonitors)
        {
            MonitorConfig entry = MonitorIdentityMatcher.Resolve(
                WharfApp.Config!.Current.Monitors,
                monitor);
            rows.Add(new Row
            {
                DeviceId = monitor.DeviceId,
                DeviceName = monitor.DeviceName,
                Name = string.IsNullOrWhiteSpace(entry.Name) ? monitor.FriendlyName : entry.Name,
                PrimaryBadgeVisibility = monitor.IsPrimary ? Visibility.Visible : Visibility.Collapsed,
                TopBar = entry.TopBar,
                Dock = entry.Dock,
            });
        }

        MonitorList.ItemsSource = rows;
    }

    private void OnTopBarToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (sender is System.Windows.Controls.Primitives.ToggleButton { Tag: string deviceId } toggle)
        {
            Monitors?.SetPartEnabled(deviceId, MonitorPart.TopBar, toggle.IsChecked == true);
        }
    }

    private void OnDockToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (sender is System.Windows.Controls.Primitives.ToggleButton { Tag: string deviceId } toggle)
        {
            Monitors?.SetPartEnabled(deviceId, MonitorPart.Dock, toggle.IsChecked == true);
        }
    }
}