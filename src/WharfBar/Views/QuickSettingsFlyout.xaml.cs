using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class QuickSettingsFlyout : UserControl
{
    private readonly AppIconService _icons = new();

    private VolumeService? _volume;
    private AudioMixerService? _mixer;
    private QuickSettingsService? _quick;
    private bool _masterSliderUpdating;
    private bool _sessionSliderUpdating;
    private bool _deviceSliderUpdating;
    private QuickSettingsPage _page = QuickSettingsPage.Main;

    public QuickSettingsFlyout()
    {
        InitializeComponent();
    }

    internal void Attach(VolumeService volume, AudioMixerService mixer, QuickSettingsService quick)
    {
        Detach();
        _volume = volume;
        _mixer = mixer;
        _quick = quick;
        _volume.Changed += OnVolumeChanged;
        _mixer.Changed += OnMixerChanged;
        _quick.Changed += OnQuickChanged;
    }

    internal void Detach()
    {
        if (_volume is not null)
        {
            _volume.Changed -= OnVolumeChanged;
        }

        if (_mixer is not null)
        {
            _mixer.Changed -= OnMixerChanged;
        }

        if (_quick is not null)
        {
            _quick.Changed -= OnQuickChanged;
        }

        _volume = null;
        _mixer = null;
        _quick = null;
    }

    internal void ShowPage(QuickSettingsPage page)
    {
        _page = page;
        MainPage.Visibility = page == QuickSettingsPage.Main ? Visibility.Visible : Visibility.Collapsed;
        MixerPage.Visibility = page == QuickSettingsPage.Mixer ? Visibility.Visible : Visibility.Collapsed;
        NetworkPage.Visibility = page == QuickSettingsPage.Network ? Visibility.Visible : Visibility.Collapsed;

        if (page == QuickSettingsPage.Mixer)
        {
            _mixer?.StartPolling();
        }
        else
        {
            _mixer?.StopPolling();
        }

        if (page is QuickSettingsPage.Main or QuickSettingsPage.Network)
        {
            _quick?.StartPolling();
        }

        RefreshAll();
    }

    internal void OnFlyoutOpened(QuickSettingsPage page)
    {
        _quick?.StartPolling();
        ShowPage(page);
    }

    internal void OnFlyoutClosed()
    {
        _mixer?.StopPolling();
        _quick?.StopPolling();
    }

    private void OnVolumeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshMaster);

    private void OnMixerChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshMixer);

    private void OnQuickChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshQuick);

    private void RefreshAll()
    {
        RefreshMaster();
        RefreshMixer();
        RefreshQuick();
    }

    private void RefreshMaster()
    {
        if (_volume is null)
        {
            return;
        }

        VolumeSnapshot snap = _volume.Snapshot;
        _masterSliderUpdating = true;
        try
        {
            MasterVolumeSlider.Value = snap.Percent;
            string glyph = snap.IsMuted || snap.Percent == 0 ? "\uE74F" : "\uE767";
            MasterMuteButton.Content = glyph;
            MasterMuteButton.ToolTip = snap.IsMuted ? "Sesi aç" : "Sessiz";
        }
        finally
        {
            _masterSliderUpdating = false;
        }
    }

    private void RefreshMixer()
    {
        if (_mixer is null || _page != QuickSettingsPage.Mixer)
        {
            return;
        }

        MixerSnapshot snap = _mixer.Snapshot;
        _deviceSliderUpdating = true;
        _sessionSliderUpdating = true;
        try
        {
            if (snap.Selected is null)
            {
                SelectedDeviceNameText.Text = "Çıkış aygıtı bulunamadı";
                SelectedDeviceNameText.ToolTip = null;
                DevicePickerButton.IsEnabled = false;
                DevicePickerList.Children.Clear();
                MixerMasterSlider.IsEnabled = false;
                MixerMasterMuteButton.IsEnabled = false;
                MixerMasterPercentText.Text = "—";
                MixerSessionList.Children.Clear();
                return;
            }

            DevicePickerButton.IsEnabled = snap.Devices.Count > 0;
            SelectedDeviceNameText.Text = snap.Selected.Name;
            SelectedDeviceNameText.ToolTip = snap.Selected.Name;

            MixerMasterMuteButton.IsEnabled = true;
            MixerMasterSlider.IsEnabled = !snap.Selected.IsMuted;
            MixerMasterMuteButton.Content =
                snap.Selected.IsMuted || snap.Selected.Percent == 0 ? "\uE74F" : "\uE767";
            MixerMasterMuteButton.ToolTip = snap.Selected.IsMuted ? "Sesi aç" : "Sessiz";
            MixerMasterSlider.Value = snap.Selected.Percent;
            MixerMasterPercentText.Text = snap.Selected.Percent.ToString();

            RebuildDevicePickerList(snap);
            RebuildSessionList(snap.Sessions);
        }
        finally
        {
            _deviceSliderUpdating = false;
            _sessionSliderUpdating = false;
        }
    }

    private void RebuildDevicePickerList(MixerSnapshot snap)
    {
        DevicePickerList.Children.Clear();
        foreach (MixerDeviceInfo device in snap.Devices)
        {
            var row = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                Tag = device.DeviceId,
                Background = device.IsDefault
                    ? TryFindResource("ControlFillColorSecondaryBrush") as Brush
                        ?? Brushes.Transparent
                    : Brushes.Transparent,
            };

            row.Child = new TextBlock
            {
                Text = device.IsDefault ? $"{device.Name} (varsayılan)" : device.Name,
                FontSize = 12,
                FontWeight = device.IsDefault ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            row.MouseLeftButtonUp += OnDevicePickerItemClick;
            DevicePickerList.Children.Add(row);
        }
    }

    private void RebuildSessionList(IReadOnlyList<AudioAppSession> sessions)
    {
        MixerSessionList.Children.Clear();
        if (sessions.Count == 0)
        {
            MixerSessionList.Children.Add(new TextBlock
            {
                Text = "Uygulama oturumu yok",
                Opacity = 0.55,
                FontSize = 12,
                Margin = new Thickness(4, 4, 0, 0),
            });
            return;
        }

        foreach (AudioAppSession session in sessions)
        {
            MixerSessionList.Children.Add(BuildSessionRow(session));
        }
    }

    private UIElement BuildSessionRow(AudioAppSession session)
    {
        var dock = new DockPanel
        {
            Margin = new Thickness(0, 6, 0, 0),
            Tag = session.SessionInstanceId,
        };

        FrameworkElement leading = BuildSessionIcon(session);
        leading.Margin = new Thickness(2, 0, 0, 0);
        DockPanel.SetDock(leading, Dock.Left);

        // Mute on icon click
        leading.Cursor = Cursors.Hand;
        leading.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _mixer?.TrySetSessionMuted(session.SessionInstanceId, !session.IsMuted);
        };
        leading.ToolTip = session.DisplayName;

        var percent = new TextBlock
        {
            Text = session.IsMuted ? "—" : session.Percent.ToString(),
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            FontSize = 12,
            Opacity = 0.9,
        };
        DockPanel.SetDock(percent, Dock.Right);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = session.Percent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Tag = session.SessionInstanceId,
            IsEnabled = !session.IsMuted,
            Opacity = session.IsMuted ? 0.45 : 1,
        };
        slider.ValueChanged += OnSessionVolumeChanged;

        dock.Children.Add(leading);
        dock.Children.Add(percent);
        dock.Children.Add(slider);
        return dock;
    }

    private FrameworkElement BuildSessionIcon(AudioAppSession session)
    {
        ImageSource? source = ResolveSessionIcon(session);
        if (source is not null)
        {
            return new Image
            {
                Source = source,
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        return new TextBlock
        {
            Text = session.IsMuted ? "\uE74F" : "\uE767",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            Width = 20,
            Height = 20,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85,
        };
    }

    private ImageSource? ResolveSessionIcon(AudioAppSession session)
    {
        string? path = session.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return _icons.GetIcon(path, 20);
        }

        // IconPath may be "C:\...\foo.dll,-123" — try the file portion.
        if (!string.IsNullOrWhiteSpace(session.IconPath))
        {
            string file = session.IconPath;
            int comma = file.IndexOf(',');
            if (comma > 0)
            {
                file = file[..comma];
            }

            if (File.Exists(file))
            {
                return _icons.GetIcon(file, 20);
            }
        }

        return null;
    }

    private void RefreshQuick()
    {
        if (_quick is null)
        {
            return;
        }

        TilesGrid.Children.Clear();
        foreach (QuickTileState tile in _quick.Tiles)
        {
            TilesGrid.Children.Add(BuildTile(tile));
        }

        NetworkDetails net = _quick.Network;
        NetworkTitleText.Text = net.Title;
        NetworkSubtitleText.Text = net.IsOnline ? net.Subtitle : "Bağlantı yok";
        NetworkGlyphLarge.Text = net.Kind switch
        {
            NetworkKind.WiFi => "\uE701",
            NetworkKind.Ethernet => "\uE839",
            NetworkKind.Other when net.IsOnline => "\uE774",
            _ => "\uE704",
        };
    }

    private UIElement BuildTile(QuickTileState tile)
    {
        var cell = new StackPanel { Margin = new Thickness(4) };

        var border = new Border
        {
            Style = TryFindResource(tile.IsActive ? "QsTileActive" : "QsTile") as Style,
            Tag = tile.Id,
            ToolTip = CreateTip(tile.Tooltip),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (tile.HasChevron)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var icon = new TextBlock
        {
            Text = tile.Glyph,
            Style = TryFindResource("QsGlyph") as Style,
            Foreground = tile.IsActive
                ? TryFindResource("TextFillColorInverseBrush") as Brush
                    ?? Brushes.Black
                : TryFindResource("TextFillColorPrimaryBrush") as Brush
                    ?? Foreground,
        };
        grid.Children.Add(icon);

        if (tile.HasChevron)
        {
            var chevron = new Border
            {
                Width = 22,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = tile.Id,
                Child = new TextBlock
                {
                    Text = "\uE76C",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.85,
                    Foreground = icon.Foreground,
                },
            };
            Grid.SetColumn(chevron, 1);
            chevron.MouseLeftButtonUp += OnTileChevronClick;
            grid.Children.Add(chevron);

            border.BorderBrush = TryFindResource("ControlStrokeColorDefaultBrush") as Brush;
            border.BorderThickness = new Thickness(0);
        }

        border.Child = grid;
        border.MouseLeftButtonUp += OnTileClick;
        cell.Children.Add(border);
        cell.Children.Add(new TextBlock
        {
            Text = tile.Label,
            Style = TryFindResource("QsTileLabel") as Style,
        });
        return cell;
    }

    private static ToolTip CreateTip(string text) => new()
    {
        Content = text,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 12,
    };

    private async void OnTileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || _quick is null)
        {
            return;
        }

        e.Handled = true;
        await _quick.ToggleTileAsync(id);
        RefreshQuick();
    }

    private void OnTileChevronClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        e.Handled = true;
        if (id is "wifi" or "bluetooth")
        {
            ShowPage(QuickSettingsPage.Network);
            return;
        }

        if (id == "project")
        {
            _ = _quick?.ToggleTileAsync("project");
        }
    }

    private void OnMasterMuteClick(object sender, RoutedEventArgs e) => _volume?.ToggleMute();

    private void OnMasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_masterSliderUpdating || _volume is null)
        {
            return;
        }

        _volume.SetPercent((int)Math.Round(e.NewValue));
    }

    private void OnOpenMixerClick(object sender, RoutedEventArgs e) => ShowPage(QuickSettingsPage.Mixer);

    private void OnBackToMainClick(object sender, RoutedEventArgs e) => ShowPage(QuickSettingsPage.Main);

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _quick?.OpenSettingsHome();

    private void OnOpenNetworkSettingsClick(object sender, RoutedEventArgs e) => _quick?.OpenNetworkSettings();

    private void OnOpenWifiSettingsClick(object sender, RoutedEventArgs e) =>
        QuickSettingsService.OpenSettingsUri("ms-settings:network-wifi");

    private void OnOpenEthernetSettingsClick(object sender, RoutedEventArgs e) =>
        QuickSettingsService.OpenSettingsUri("ms-settings:network-ethernet");

    private void OnDevicePickerClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!DevicePickerButton.IsEnabled || _mixer is null)
        {
            return;
        }

        DevicePickerPopup.IsOpen = !DevicePickerPopup.IsOpen;
    }

    private void OnDevicePickerItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || _mixer is null)
        {
            return;
        }

        e.Handled = true;
        DevicePickerPopup.IsOpen = false;
        if (!_mixer.TrySetDefaultDevice(id))
        {
            RefreshMixer();
        }
    }

    private void OnMixerMasterMuteClick(object sender, RoutedEventArgs e) =>
        _mixer?.ToggleMasterMute();

    private void OnMixerMasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_deviceSliderUpdating || _mixer is null)
        {
            return;
        }

        int percent = (int)Math.Round(e.NewValue);
        _mixer.SetMasterPercent(percent);
        MixerMasterPercentText.Text = percent.ToString();
    }

    private void OnSessionVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sessionSliderUpdating || _mixer is null || sender is not Slider { Tag: string id })
        {
            return;
        }

        _mixer.TrySetSessionPercent(id, (int)Math.Round(e.NewValue));
    }
}
