using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private bool _devicePickerExpanded;
    private int _activeSessionDrags;
    private bool _masterDragging;
    private QuickSettingsPage _page = QuickSettingsPage.Main;

    private sealed class SessionRowState
    {
        internal required string SessionInstanceId { get; init; }
        internal bool IsMuted { get; set; }
        internal required string DisplayName { get; set; }
        internal required Slider Slider { get; init; }
        internal required TextBlock PercentText { get; init; }
        internal required FrameworkElement IconHost { get; init; }
    }

    public QuickSettingsFlyout()
    {
        InitializeComponent();
        MixerMasterSlider.AddHandler(
            Thumb.DragStartedEvent,
            new DragStartedEventHandler((_, _) => _masterDragging = true),
            handledEventsToo: true);
        MixerMasterSlider.AddHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) =>
            {
                _masterDragging = false;
                _mixer?.RefreshNow();
            }),
            handledEventsToo: true);
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
            SetDevicePickerExpanded(false);
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
        SetDevicePickerExpanded(false);
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
            MasterMuteGlyph.Text = glyph;
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
                SetDevicePickerExpanded(false);
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
            MixerMasterMuteGlyph.Text =
                snap.Selected.IsMuted || snap.Selected.Percent == 0 ? "\uE74F" : "\uE767";
            MixerMasterMuteButton.ToolTip = snap.Selected.IsMuted ? "Sesi aç" : "Sessiz";
            if (!_masterDragging && !MixerMasterSlider.IsMouseCaptureWithin)
            {
                MixerMasterSlider.Value = snap.Selected.Percent;
                MixerMasterPercentText.Text = snap.Selected.Percent.ToString();
            }

            RebuildDevicePickerList(snap);
            // Never tear down session sliders mid-drag — that made app volume bars unusable.
            if (_activeSessionDrags == 0)
            {
                SyncSessionList(snap.Sessions);
            }
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

    private void SyncSessionList(IReadOnlyList<AudioAppSession> sessions)
    {
        if (sessions.Count == 0)
        {
            if (MixerSessionList.Children.Count == 1
                && MixerSessionList.Children[0] is TextBlock empty
                && empty.Text == "Uygulama oturumu yok")
            {
                return;
            }

            MixerSessionList.Children.Clear();
            MixerSessionList.Children.Add(new TextBlock
            {
                Text = "Uygulama oturumu yok",
                Opacity = 0.55,
                FontSize = 12,
                Margin = new Thickness(4, 4, 0, 0),
            });
            return;
        }

        if (TryUpdateSessionRowsInPlace(sessions))
        {
            return;
        }

        MixerSessionList.Children.Clear();
        foreach (AudioAppSession session in sessions)
        {
            MixerSessionList.Children.Add(BuildSessionRow(session));
        }
    }

    private bool TryUpdateSessionRowsInPlace(IReadOnlyList<AudioAppSession> sessions)
    {
        if (MixerSessionList.Children.Count != sessions.Count)
        {
            return false;
        }

        for (int i = 0; i < sessions.Count; i++)
        {
            if (MixerSessionList.Children[i] is not DockPanel { Tag: SessionRowState state }
                || !string.Equals(
                    state.SessionInstanceId,
                    sessions[i].SessionInstanceId,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (int i = 0; i < sessions.Count; i++)
        {
            ApplySessionToRow((DockPanel)MixerSessionList.Children[i], sessions[i]);
        }

        return true;
    }

    private void ApplySessionToRow(DockPanel dock, AudioAppSession session)
    {
        if (dock.Tag is not SessionRowState state)
        {
            return;
        }

        state.IsMuted = session.IsMuted;
        if (!string.Equals(state.DisplayName, session.DisplayName, StringComparison.Ordinal))
        {
            state.DisplayName = session.DisplayName;
            SetSessionIconTip(state.IconHost, session.DisplayName);
        }

        state.PercentText.Text = session.IsMuted ? "—" : session.Percent.ToString();
        state.Slider.IsEnabled = !session.IsMuted;
        state.Slider.Opacity = session.IsMuted ? 0.45 : 1;

        if (!state.Slider.IsMouseCaptureWithin && _activeSessionDrags == 0)
        {
            // Caller (RefreshMixer) already holds _sessionSliderUpdating; do not clear it here.
            state.Slider.Value = session.Percent;
        }
    }

    private UIElement BuildSessionRow(AudioAppSession session)
    {
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
            IsMoveToPointEnabled = true,
            IsEnabled = !session.IsMuted,
            Opacity = session.IsMuted ? 0.45 : 1,
        };

        // Hit target larger than 20px icon; Segoe UI tip (MDL2 TextBlock must not own the tip).
        var iconHost = new Border
        {
            Width = 28,
            Height = 28,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = BuildSessionIcon(session),
        };
        DockPanel.SetDock(iconHost, Dock.Left);
        SetSessionIconTip(iconHost, session.DisplayName);

        var state = new SessionRowState
        {
            SessionInstanceId = session.SessionInstanceId,
            IsMuted = session.IsMuted,
            DisplayName = session.DisplayName,
            Slider = slider,
            PercentText = percent,
            IconHost = iconHost,
        };
        slider.Tag = state;
        slider.ValueChanged += OnSessionVolumeChanged;
        slider.AddHandler(
            Thumb.DragStartedEvent,
            new DragStartedEventHandler(OnSessionSliderDragStarted),
            handledEventsToo: true);
        slider.AddHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler(OnSessionSliderDragCompleted),
            handledEventsToo: true);

        var dock = new DockPanel
        {
            Margin = new Thickness(0, 6, 0, 0),
            Tag = state,
        };

        // Mute on icon click — read live mute from row state (in-place updates).
        iconHost.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (dock.Tag is not SessionRowState row)
            {
                return;
            }

            _mixer?.TrySetSessionMuted(row.SessionInstanceId, !row.IsMuted);
        };

        dock.Children.Add(iconHost);
        dock.Children.Add(percent);
        dock.Children.Add(slider);
        return dock;
    }

    private static void SetSessionIconTip(FrameworkElement host, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            host.ToolTip = null;
            return;
        }

        host.ToolTip = CreateTip(displayName);
        ToolTipService.SetInitialShowDelay(host, 250);
        ToolTipService.SetShowDuration(host, 8000);
        ToolTipService.SetPlacement(host, System.Windows.Controls.Primitives.PlacementMode.Top);
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
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
        }

        return new TextBlock
        {
            Text = session.IsMuted ? "\uE74F" : "\uE767",
            FontFamily = SystemVersion.IconFontFamily,
            FontSize = 16,
            Width = 20,
            Height = 20,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85,
            IsHitTestVisible = false,
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
            Style = tile.IsEnabled
                ? TryFindResource(tile.IsActive ? "QsTileActive" : "QsTile") as Style
                : TryFindResource("QsTileDisabled") as Style,
            Tag = tile.Id,
            ToolTip = CreateTip(tile.Tooltip),
            IsEnabled = tile.IsEnabled,
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
                    FontFamily = SystemVersion.IconFontFamily,
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
        FontFamily = SystemVersion.TextFontFamily,
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

        SetDevicePickerExpanded(!_devicePickerExpanded);
    }

    private void OnDevicePickerItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || _mixer is null)
        {
            return;
        }

        e.Handled = true;
        SetDevicePickerExpanded(false);
        if (!_mixer.TrySetDefaultDevice(id))
        {
            RefreshMixer();
        }
    }

    private void SetDevicePickerExpanded(bool expanded)
    {
        _devicePickerExpanded = expanded;
        DevicePickerPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        // Chevron down (E70D) when collapsed; chevron up (E70E) when open.
        DevicePickerChevron.Text = expanded ? "\uE70E" : "\uE70D";
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

    private void OnSessionSliderDragStarted(object sender, DragStartedEventArgs e) =>
        _activeSessionDrags++;

    private void OnSessionSliderDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _activeSessionDrags = Math.Max(0, _activeSessionDrags - 1);
        if (_activeSessionDrags == 0)
        {
            _mixer?.RefreshNow();
        }
    }

    private void OnSessionVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sessionSliderUpdating
            || _mixer is null
            || sender is not Slider { Tag: SessionRowState state })
        {
            return;
        }

        int percent = (int)Math.Round(e.NewValue);
        // Apply Core Audio without raising Changed — rebuilding the list mid-drag tore down
        // the Slider and made app volume bars feel broken.
        if (_mixer.TrySetSessionPercent(state.SessionInstanceId, percent, refreshUi: false))
        {
            state.PercentText.Text = percent.ToString();
        }
    }
}
