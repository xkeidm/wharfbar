using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class QuickSettingsFlyout : UserControl
{
    private readonly AppIconService _icons = new();

    private VolumeService? _volume;
    private AudioMixerService? _mixer;
    private QuickSettingsService? _quick;
    private WifiService? _wifi;
    private BluetoothService? _bluetooth;
    private ProjectionService? _projection;
    private NetworkDetailsService? _networkDetails;
    private WifiNetworkInfo? _pendingWifiConnect;
    private bool _masterSliderUpdating;
    private bool _sessionSliderUpdating;
    private bool _deviceSliderUpdating;
    private bool _devicePickerExpanded;
    private int _activeSessionDrags;
    private bool _masterDragging;
    private QuickSettingsPage _page = QuickSettingsPage.Main;
    private string? _projectError;
    private bool _flyoutOpen;
    private int _pageMotionGeneration;

    private sealed class SessionRowState
    {
        internal required string SessionInstanceId { get; init; }
        internal bool IsMuted { get; set; }
        internal required string DisplayName { get; set; }
        internal required Slider Slider { get; init; }
        internal required TextBlock PercentText { get; init; }
        internal required FrameworkElement IconHost { get; init; }
    }

    private sealed class TileUi
    {
        internal required string Id { get; init; }
        internal required StackPanel Cell { get; init; }
        internal required Border Border { get; init; }
        internal required TextBlock Icon { get; init; }
        internal required TextBlock Label { get; init; }
        internal bool IsActive { get; set; }
        internal required SolidColorBrush BaseBrush { get; init; }
        internal required SolidColorBrush AccentBrush { get; init; }
    }

    private readonly Dictionary<string, TileUi> _tiles = new();

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

    internal void Attach(VolumeService volume, AudioMixerService mixer, QuickSettingsService quick,
        WifiService wifi, BluetoothService bluetooth, ProjectionService projection,
        NetworkDetailsService networkDetails)
    {
        Detach();
        _volume = volume;
        _mixer = mixer;
        _quick = quick;
        _wifi = wifi;
        _bluetooth = bluetooth;
        _projection = projection;
        _networkDetails = networkDetails;
        _volume.Changed += OnVolumeChanged;
        _mixer.Changed += OnMixerChanged;
        _quick.Changed += OnQuickChanged;
        _wifi.Changed += OnWifiChanged;
        _bluetooth.Changed += OnBluetoothChanged;
        _networkDetails.Changed += OnNetworkDetailsChanged;
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

        if (_wifi is not null)
        {
            _wifi.Changed -= OnWifiChanged;
        }

        if (_bluetooth is not null)
        {
            _bluetooth.Changed -= OnBluetoothChanged;
        }

        if (_networkDetails is not null)
        {
            _networkDetails.Changed -= OnNetworkDetailsChanged;
        }

        _volume = null;
        _mixer = null;
        _quick = null;
        _wifi = null;
        _bluetooth = null;
        _projection = null;
        _networkDetails = null;
    }

    internal bool IsMixerPage => _page == QuickSettingsPage.Mixer;

    internal void ShowPage(QuickSettingsPage page)
    {
        QuickSettingsPage from = _page;
        QuickSettingsPageShift shift = QuickSettingsPageMotion.Shift(from, page, _flyoutOpen);
        StopPageMotion();
        _page = page;

        if (page == QuickSettingsPage.Mixer)
        {
            _mixer?.StartPolling();
        }
        else
        {
            SetDevicePickerExpanded(false);
            _mixer?.StopPolling();
        }

        if (page == QuickSettingsPage.Wifi)
        {
            _wifi?.StartPolling();
            RefreshWifiPage();
        }
        else
        {
            _wifi?.StopPolling();
            WifiPasswordPanel.Visibility = Visibility.Collapsed;
            ClearWifiStatus();
        }

        if (page == QuickSettingsPage.Bluetooth)
        {
            _bluetooth?.StartPolling();
            RefreshBluetoothPage();
        }
        else
        {
            _bluetooth?.StopPolling();
        }

        if (page == QuickSettingsPage.Network)
        {
            _networkDetails?.StartPolling();
        }
        else
        {
            _networkDetails?.StopPolling();
        }

        if (page == QuickSettingsPage.Project)
        {
            RefreshProjectPage();
        }

        if (page is QuickSettingsPage.Main or QuickSettingsPage.Network)
        {
            _quick?.StartPolling();
        }

        if (!shift.Animate)
        {
            RevealPage(page);
        }
        else
        {
            AnimatePageChange(PageElement(from), PageElement(page), shift);
        }

        RefreshAll();
    }

    internal void OnFlyoutOpened(QuickSettingsPage page)
    {
        FlyoutRoot.Background = FlyoutSurface.Create();
        _quick?.StartPolling();
        // The popup's own entrance already fades the panel in. Don't also slide the first page.
        _flyoutOpen = false;
        ShowPage(page);
        _flyoutOpen = true;
    }

    internal void OnFlyoutClosed()
    {
        _flyoutOpen = false;
        StopPageMotion();
        SetDevicePickerExpanded(false);
        _mixer?.StopPolling();
        _quick?.StopPolling();
        _wifi?.StopPolling();
        _bluetooth?.StopPolling();
        _networkDetails?.StopPolling();
        WifiPasswordPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// In-panel page change: forward slides out to the left and the new page enters from
    /// the right; back does the reverse; peer pages cross-fade. A click mid-transition
    /// drops the previous outgoing page so opacity and hit-testing cannot stick.
    /// </summary>
    private void AnimatePageChange(FrameworkElement outgoing, FrameworkElement incoming, QuickSettingsPageShift shift)
    {
        foreach (FrameworkElement page in AllPages())
        {
            page.Visibility = ReferenceEquals(page, outgoing) || ReferenceEquals(page, incoming)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        ResetPageMotion(outgoing);
        ResetPageMotion(incoming);
        outgoing.IsHitTestVisible = false;
        Panel.SetZIndex(outgoing, 0);
        Panel.SetZIndex(incoming, 1);

        TranslateTransform outgoingTransform = EnsureTranslate(outgoing);
        TranslateTransform incomingTransform = EnsureTranslate(incoming);
        outgoingTransform.X = shift.OutgoingFromX;
        incomingTransform.X = shift.IncomingFromX;
        outgoing.Opacity = 1;
        incoming.Opacity = 0;
        LockPageHostTo(incoming);

        int generation = ++_pageMotionGeneration;
        IEasingFunction ease = QuickSettingsPageMotion.CreateEase();
        TimeSpan duration = QuickSettingsPageMotion.Duration;
        var outgoingFade = new DoubleAnimation(1, 0, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        outgoingFade.Completed += (_, _) => FinishPageMotion(generation, outgoing, incoming);

        outgoingTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(
            shift.OutgoingFromX,
            shift.OutgoingToX,
            duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        });
        incomingTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(
            shift.IncomingFromX,
            shift.IncomingToX,
            duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        });
        outgoing.BeginAnimation(UIElement.OpacityProperty, outgoingFade);
        incoming.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    private void FinishPageMotion(int generation, FrameworkElement outgoing, FrameworkElement incoming)
    {
        if (generation != _pageMotionGeneration)
        {
            return;
        }

        _pageMotionGeneration++;
        PageHost.Height = double.NaN;
        ResetPageMotion(outgoing);
        ResetPageMotion(incoming);
        outgoing.Visibility = Visibility.Collapsed;
        incoming.Visibility = Visibility.Visible;
    }

    private void StopPageMotion()
    {
        _pageMotionGeneration++;
        PageHost.Height = double.NaN;
        FrameworkElement current = PageElement(_page);
        foreach (FrameworkElement page in AllPages())
        {
            ResetPageMotion(page);
            page.Visibility = ReferenceEquals(page, current)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void RevealPage(QuickSettingsPage page)
    {
        FrameworkElement current = PageElement(page);
        foreach (FrameworkElement candidate in AllPages())
        {
            ResetPageMotion(candidate);
            candidate.Visibility = ReferenceEquals(candidate, current)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void LockPageHostTo(FrameworkElement incoming)
    {
        double width = PageHost.ActualWidth > 0 ? PageHost.ActualWidth : double.PositiveInfinity;
        incoming.Measure(new Size(width, double.PositiveInfinity));
        double height = incoming.DesiredSize.Height;
        if (height > 0 && !double.IsPositiveInfinity(height))
        {
            PageHost.Height = height;
        }
    }

    private static void ResetPageMotion(FrameworkElement page)
    {
        page.BeginAnimation(UIElement.OpacityProperty, null);
        TranslateTransform transform = EnsureTranslate(page);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        page.Opacity = 1;
        page.IsHitTestVisible = true;
        Panel.SetZIndex(page, 0);
    }

    private static TranslateTransform EnsureTranslate(FrameworkElement page)
    {
        if (page.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }

        var created = new TranslateTransform();
        page.RenderTransform = created;
        return created;
    }

    private FrameworkElement PageElement(QuickSettingsPage page) => page switch
    {
        QuickSettingsPage.Mixer => MixerPage,
        QuickSettingsPage.Network => NetworkPage,
        QuickSettingsPage.Wifi => WifiPage,
        QuickSettingsPage.Bluetooth => BluetoothPage,
        QuickSettingsPage.Project => ProjectPage,
        _ => MainPage,
    };

    private IEnumerable<FrameworkElement> AllPages()
    {
        yield return MainPage;
        yield return MixerPage;
        yield return NetworkPage;
        yield return WifiPage;
        yield return BluetoothPage;
        yield return ProjectPage;
    }

    private void OnVolumeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshMaster);

    private void OnMixerChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshMixer);

    private void OnQuickChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        RefreshQuick();
        if (_page == QuickSettingsPage.Wifi)
        {
            RefreshWifiPage();
        }
        else if (_page == QuickSettingsPage.Bluetooth)
        {
            RefreshBluetoothPage();
        }
    });

    private void OnWifiChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshWifiPage);

    private void OnBluetoothChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshBluetoothPage);

    private void OnNetworkDetailsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshNetworkPage);

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
            MasterVolumeSlider.IsEnabled = snap.IsAvailable;
            MasterMuteButton.IsEnabled = snap.IsAvailable;
            if (snap.IsAvailable)
            {
                MasterVolumeSlider.Value = snap.Percent;
            }

            string glyph = snap.IsMuted || snap.Percent == 0 ? "\uE74F" : "\uE767";
            MasterMuteGlyph.Text = glyph;
            MasterMuteButton.ToolTip = snap.IsAvailable
                ? snap.IsMuted ? "Sesi aç" : "Sessiz"
                : "Ses aygıtı yok";
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

        IReadOnlyList<QuickTileState> tiles = _quick.Tiles;

        if (tiles.Count != _tiles.Count || tiles.Any(t => !_tiles.ContainsKey(t.Id)))
        {
            TilesGrid.Children.Clear();
            _tiles.Clear();
            foreach (QuickTileState tile in tiles)
            {
                TileUi ui = BuildTile(tile);
                _tiles[tile.Id] = ui;
                TilesGrid.Children.Add(ui.Cell);
            }
        }
        else
        {
            foreach (QuickTileState tile in tiles)
            {
                UpdateTile(_tiles[tile.Id], tile);
            }
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

    private TileUi BuildTile(QuickTileState tile)
    {
        var cell = new StackPanel { Margin = new Thickness(4) };

        SolidColorBrush baseBrush = TryFindResource("ControlFillColorDefaultBrush") as SolidColorBrush
            ?? Foreground as SolidColorBrush
            ?? Brushes.Transparent;
        SolidColorBrush accentBrush = TryFindResource("AccentFillColorDefaultBrush") as SolidColorBrush
            ?? Foreground as SolidColorBrush
            ?? Brushes.Transparent;

        var border = new Border
        {
            Style = TryFindResource("QsTile") as Style,
            Background = tile.IsEnabled
                ? tile.IsActive ? accentBrush : baseBrush
                : baseBrush,
            Tag = tile.Id,
            ToolTip = CreateTip(tile.Tooltip),
            IsEnabled = tile.IsEnabled,
        };
        if (!tile.IsEnabled)
        {
            border.Style = TryFindResource("QsTileDisabled") as Style;
            border.Opacity = 0.45;
        }

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
                ? TryFindResource("TextFillColorInverseBrush") as Brush ?? Brushes.Black
                : TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Foreground,
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
        var label = new TextBlock
        {
            Text = tile.Label,
            Style = TryFindResource("QsTileLabel") as Style,
        };
        cell.Children.Add(label);

        return new TileUi
        {
            Id = tile.Id,
            Cell = cell,
            Border = border,
            Icon = icon,
            Label = label,
            IsActive = tile.IsActive,
            BaseBrush = baseBrush,
            AccentBrush = accentBrush,
        };
    }

    private void UpdateTile(TileUi ui, QuickTileState tile)
    {
        if (ui.Label.Text != tile.Label)
        {
            ui.Label.Text = tile.Label;
        }

        if (ui.Border.ToolTip is ToolTip tip)
        {
            tip.Content = tile.Tooltip;
        }
        else if (ui.Border.ToolTip is not string)
        {
            ui.Border.ToolTip = CreateTip(tile.Tooltip);
        }

        bool activeChanged = ui.IsActive != tile.IsActive;
        if (activeChanged)
        {
            ui.IsActive = tile.IsActive;
            ui.Icon.Foreground = tile.IsActive
                ? TryFindResource("TextFillColorInverseBrush") as Brush ?? Brushes.Black
                : TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Foreground;
            SolidColorBrush? from = ui.Border.Background as SolidColorBrush;
            var target = new SolidColorBrush((tile.IsActive ? ui.AccentBrush : ui.BaseBrush).Color);
            SetTileBackground(ui, target);
            AnimateTileToggle(ui, from, target, tile.IsActive);
        }
        else
        {
            SetTileBackground(ui, tile.IsActive ? ui.AccentBrush : ui.BaseBrush);
        }

        ui.Border.IsEnabled = tile.IsEnabled;
        if (!tile.IsEnabled)
        {
            ui.Border.Style = TryFindResource("QsTileDisabled") as Style;
            ui.Border.Opacity = 0.45;
            ui.Border.Cursor = Cursors.Arrow;
        }
        else
        {
            ui.Border.Style = TryFindResource("QsTile") as Style;
            ui.Border.Opacity = 1;
            ui.Border.Cursor = Cursors.Hand;
        }
    }

    private static void SetTileBackground(TileUi ui, Brush brush)
    {
        ui.Border.Background = brush;
    }

    private void AnimateTileToggle(TileUi ui, SolidColorBrush? from, SolidColorBrush target, bool activating)
    {
        var group = new TransformGroup();
        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform(0);
        group.Children.Add(scale);
        group.Children.Add(rotate);
        ui.Icon.RenderTransform = group;
        ui.Icon.RenderTransformOrigin = new Point(0.5, 0.5);

        var storyboard = new Storyboard();

        var pop = new DoubleAnimation(
                fromValue: 1,
                toValue: activating ? 1.15 : 0.9,
                duration: TimeSpan.FromMilliseconds(90))
        {
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(pop, ui.Icon);
        Storyboard.SetTargetProperty(pop, new PropertyPath("RenderTransform.Children[0].ScaleX"));
        storyboard.Children.Add(pop);

        if (from is not null && from.Color != target.Color)
        {
            var colorAnim = new ColorAnimation(from.Color, target.Color, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(colorAnim, target);
            Storyboard.SetTargetProperty(colorAnim, new PropertyPath("Color"));
            storyboard.Children.Add(colorAnim);
        }

        storyboard.Begin();
    }

    private static ToolTip CreateTip(string text) => new()
    {
        Content = text,
        FontFamily = SystemVersion.TextFontFamily,
        FontSize = 12,
    };

    private async void OnTileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id, IsEnabled: true } || _quick is null)
        {
            return;
        }

        e.Handled = true;
        if (id == "project")
        {
            ShowPage(QuickSettingsPage.Project);
            return;
        }

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
        if (id is "wifi")
        {
            ShowPage(QuickSettingsPage.Wifi);
            return;
        }

        if (id == "bluetooth")
        {
            ShowPage(QuickSettingsPage.Bluetooth);
            return;
        }

        if (id == "project")
        {
            ShowPage(QuickSettingsPage.Project);
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

    private void RefreshWifiPage()
    {
        if (_wifi is null || _page != QuickSettingsPage.Wifi)
        {
            return;
        }

        bool radioOn = _quick?.IsWifiRadioOn ?? true;
        if (_quick is not null)
        {
            WifiSummarySubtitle.Text = !radioOn
                ? "Wi-Fi kapalı"
                : !_wifi.IsAvailable
                    ? "Adaptör bulunamadı"
                    : _wifi.Networks.FirstOrDefault(n => n.IsConnected)?.Ssid ?? "Bağlı değil";
        }

        WifiNetworkList.Children.Clear();
        if (radioOn)
        {
            foreach (WifiNetworkInfo network in _wifi.Networks)
            {
                WifiNetworkList.Children.Add(BuildWifiRow(network));
            }
        }

        if (WifiNetworkList.Children.Count == 0 && (_wifi.IsAvailable || !radioOn))
        {
            WifiNetworkList.Children.Add(new TextBlock
            {
                Text = !radioOn ? "Wi-Fi kapalı" : "Ağlar aranıyor…",
                Opacity = 0.55,
                FontSize = 12,
                Margin = new Thickness(4, 4, 0, 0),
            });
        }
    }

    private UIElement BuildWifiRow(WifiNetworkInfo network)
    {
        int bars = WifiService.SignalBars(network.Signal);
        string signalGlyph = bars switch
        {
            4 => "\uE854",
            3 => "\uE853",
            2 => "\uE852",
            _ => "\uE851",
        };

        var border = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 6),
            Background = network.IsConnected
                ? TryFindResource("AccentFillColorDefaultBrush") as Brush
                    ?? TryFindResource("ControlFillColorDefaultBrush") as Brush
                : TryFindResource("ControlFillColorDefaultBrush") as Brush,
            Tag = network,
        };

        var dock = new DockPanel();

        var signal = new TextBlock
        {
            Text = signalGlyph,
            FontFamily = SystemVersion.IconFontFamily,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(signal, Dock.Right);

        var lockIcon = network.IsSecured ? "\uE72E" : "\uE730";
        var name = new TextBlock
        {
            Text = network.Ssid,
            FontSize = 13,
            FontWeight = network.IsConnected ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var security = new TextBlock
        {
            Text = lockIcon,
            FontFamily = SystemVersion.IconFontFamily,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Opacity = 0.75,
        };
        DockPanel.SetDock(security, Dock.Left);
        if (!network.IsSecured)
        {
            security.Visibility = Visibility.Collapsed;
        }

        var badge = network.IsConnected
            ? "Bağlı"
            : network.IsKnown
                ? "Kayıtlı"
                : string.Empty;
        if (badge.Length > 0)
        {
            var badgeText = new TextBlock
            {
                Text = badge,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Opacity = 0.7,
            };
            dock.Children.Add(badgeText);
        }

        dock.Children.Add(signal);
        dock.Children.Add(security);
        dock.Children.Add(name);
        border.Child = dock;
        border.MouseLeftButtonUp += OnWifiRowClick;
        return border;
    }

    private async void OnWifiRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WifiNetworkInfo network } || _wifi is null)
        {
            return;
        }

        e.Handled = true;
        if (network.IsConnected)
        {
            (bool disconnected, string? disconnectError) = _wifi.TryDisconnect();
            if (disconnected)
            {
                ClearWifiStatus();
            }
            else
            {
                ShowWifiError(disconnectError ?? "Bağlantı kesilemedi");
            }

            return;
        }

        if (WifiService.ConnectBlockReason(network) is { } blocked)
        {
            ShowWifiError(blocked);
            return;
        }

        if (network.IsKnown || !network.IsSecured)
        {
            await ConfirmWifiConnectAsync(network, password: null);
            return;
        }

        _pendingWifiConnect = network;
        WifiPasswordSsid.Text = network.Ssid;
        ClearWifiStatus();
        WifiPasswordBox.Password = string.Empty;
        WifiPasswordPanel.Visibility = Visibility.Visible;
        WifiPasswordBox.Focus();
    }

    private async void OnWifiConnectClick(object sender, RoutedEventArgs e)
    {
        if (_wifi is null || _pendingWifiConnect is not { } network)
        {
            return;
        }

        string password = WifiPasswordBox.Password;
        if (await ConfirmWifiConnectAsync(network, password))
        {
            WifiPasswordPanel.Visibility = Visibility.Collapsed;
            _pendingWifiConnect = null;
        }
    }

    private async Task<bool> ConfirmWifiConnectAsync(WifiNetworkInfo network, string? password)
    {
        if (_wifi is null)
        {
            return false;
        }

        ShowWifiError("Bağlanıyor…");
        (bool ok, string? error) = await Task.Run(() => _wifi.TryConnect(network, password));
        if (!ok)
        {
            ShowWifiError(error ?? "Bağlanılamadı");
            return false;
        }

        bool connected = await Task.Run(() => _wifi.WaitUntilConnected(network.Ssid, TimeSpan.FromSeconds(8)));
        if (!connected)
        {
            ShowWifiError("Bağlanılamadı");
            RefreshWifiPage();
            return false;
        }

        ClearWifiStatus();
        RefreshWifiPage();
        return true;
    }

    private void OnWifiCancelClick(object sender, RoutedEventArgs e)
    {
        WifiPasswordPanel.Visibility = Visibility.Collapsed;
        _pendingWifiConnect = null;
        ClearWifiStatus();
    }

    private void ShowWifiError(string message)
    {
        WifiErrorText.Text = message;
        WifiStatusText.Text = message;
        WifiStatusText.Visibility = Visibility.Visible;
    }

    private void ClearWifiStatus()
    {
        WifiErrorText.Text = string.Empty;
        WifiStatusText.Text = string.Empty;
        WifiStatusText.Visibility = Visibility.Collapsed;
    }

    private void RefreshNetworkPage()
    {
        if (_networkDetails is null || _page != QuickSettingsPage.Network)
        {
            return;
        }

        NetworkDetailsSnapshot snap = _networkDetails.Snapshot;

        TrafficDownloadText.Text = snap.DownloadMbps is { } down
            ? NetworkDetailsService.FormatMbps(down)
            : "—";
        TrafficUploadText.Text = snap.UploadMbps is { } up
            ? NetworkDetailsService.FormatMbps(up)
            : "—";

        string kindLabel = snap.Kind switch
        {
            NetworkKind.WiFi => "Wi-Fi",
            NetworkKind.Ethernet => "Ethernet",
            NetworkKind.Offline => "Bağlantı yok",
            _ => "Diğer ağ",
        };

        SetNetRow(NetAdapterName, snap.AdapterName);
        SetNetRow(NetKind, snap.IsOnline || snap.Kind != NetworkKind.Offline ? kindLabel : null);
        SetNetRow(NetIpv4, snap.Ipv4);
        SetNetRow(NetIpv6, snap.Ipv6);
        SetNetRow(NetGateway, snap.Gateway);
        SetNetRow(NetDns, snap.Dns);
        SetNetRow(NetMac, snap.Mac);
        SetNetRow(NetLinkSpeed, snap.LinkSpeed);
    }

    private static void SetNetRow(TextBlock target, string? value)
    {
        target.Text = string.IsNullOrWhiteSpace(value) ? "—" : value;
        target.ToolTip = string.IsNullOrWhiteSpace(value) ? null : CreateTip(value);
    }

    private void RefreshBluetoothPage()
    {
        if (_bluetooth is null || _page != QuickSettingsPage.Bluetooth)
        {
            return;
        }

        if (_quick is not null)
        {
            BtSummarySubtitle.Text = _quick.IsBluetoothRadioOn ? "Açık" : "Kapalı";
        }

        BluetoothDeviceList.Children.Clear();
        foreach (BluetoothDeviceInfo device in _bluetooth.Devices)
        {
            var border = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 6),
                Background = TryFindResource("ControlFillColorDefaultBrush") as Brush,
                Tag = device,
            };
            border.Child = new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = device.Name,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            };
            if (device.IsConnected)
            {
                var badge = new TextBlock
                {
                    Text = "Bağlı",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Opacity = 0.7,
                };
                DockPanel.SetDock(badge, Dock.Right);
                ((DockPanel)border.Child).Children.Add(badge);
            }

            border.MouseLeftButtonUp += OnBluetoothDeviceClick;
            BluetoothDeviceList.Children.Add(border);
        }

        if (BluetoothDeviceList.Children.Count == 0)
        {
            bool radioOn = _quick?.IsBluetoothRadioOn ?? true;
            BluetoothDeviceList.Children.Add(new TextBlock
            {
                Text = !radioOn ? "Bluetooth kapalı — cihazları görmek için açın" : "Eşleşmiş cihaz yok",
                Opacity = 0.55,
                FontSize = 12,
                Margin = new Thickness(4, 4, 0, 0),
            });
        }
    }

    private void OnBluetoothDeviceClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BluetoothDeviceInfo })
        {
            return;
        }

        e.Handled = true;
        QuickSettingsService.OpenSettingsUri("ms-settings:bluetooth");
    }

    private void OnOpenBluetoothSettingsClick(object sender, RoutedEventArgs e) =>
        QuickSettingsService.OpenSettingsUri("ms-settings:bluetooth");

    private static readonly (ProjectionMode Mode, string Label)[] ProjectModes =
    [
        (ProjectionMode.InternalOnly, "Yalnızca PC ekranı"),
        (ProjectionMode.Clone, "Çoğalt"),
        (ProjectionMode.Extend, "Genişlet"),
        (ProjectionMode.ExternalOnly, "Yalnızca 2. ekran"),
    ];

    private void RefreshProjectPage()
    {
        if (_projection is null || _page != QuickSettingsPage.Project)
        {
            return;
        }

        _projection.Refresh();
        int monitors = DisplayTopology.ReadMonitorMetrics().Monitors;
        ProjectModeList.Children.Clear();
        foreach ((ProjectionMode mode, string label) in ProjectModes)
        {
            bool active = _projection.Current == mode;
            bool needsSecond = mode != ProjectionMode.InternalOnly && monitors < 2;
            var border = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                Cursor = needsSecond ? Cursors.Arrow : Cursors.Hand,
                Opacity = needsSecond ? 0.45 : 1,
                Margin = new Thickness(0, 0, 0, 6),
                Background = active
                    ? TryFindResource("AccentFillColorDefaultBrush") as Brush
                    : TryFindResource("ControlFillColorDefaultBrush") as Brush,
                Tag = mode,
            };

            var glyph = new TextBlock
            {
                Text = active ? "\uE73E" : "\uE756",
                FontFamily = SystemVersion.IconFontFamily,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = active
                    ? TryFindResource("TextFillColorInverseBrush") as Brush
                    : null,
            };
            DockPanel.SetDock(glyph, Dock.Left);

            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };

            var dock = new DockPanel();
            dock.Children.Add(glyph);
            dock.Children.Add(labelBlock);
            border.Child = dock;
            border.MouseLeftButtonUp += OnProjectModeClick;
            ProjectModeList.Children.Add(border);
        }

        if (!string.IsNullOrEmpty(_projectError))
        {
            ProjectModeList.Children.Add(new TextBlock
            {
                Text = _projectError,
                FontSize = 12,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 2, 0, 0),
            });
        }
    }

    private void OnProjectModeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProjectionMode mode } || _projection is null)
        {
            return;
        }

        e.Handled = true;
        int monitors = DisplayTopology.ReadMonitorMetrics().Monitors;
        if (mode != ProjectionMode.InternalOnly && monitors < 2)
        {
            _projectError = "İkinci ekran yok";
            RefreshProjectPage();
            return;
        }

        if (_projection.TryApply(mode))
        {
            _projectError = null;
            RefreshProjectPage();
            return;
        }

        _projectError = "Bu mod uygulanamadı";
        RefreshProjectPage();
    }

    private void OnOpenDisplaySettingsClick(object sender, RoutedEventArgs e) =>
        QuickSettingsService.OpenSettingsUri("ms-settings:display");

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
