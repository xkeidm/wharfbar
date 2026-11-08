using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// Island (clipboard history + dropped files) settings: visibility, retention mode, limits and
/// maintenance actions. Every change saves the config and applies it live through <see cref="IslandService.ApplyConfig"/>
/// — because <c>Config.Enabled</c> changes only via ApplyConfig, the
/// clipboard listener never starts without this call and the pill visibility is not updated either.
/// </summary>
public partial class IslandPage : Page
{
    private bool _isLoading = true;
    private bool _syncingLimits;

    public IslandPage()
    {
        InitializeComponent();
        LoadSettings();

        EnabledToggle.Checked += OnEnabledChanged;
        EnabledToggle.Unchecked += OnEnabledChanged;
        PersistenceCombo.SelectionChanged += OnPersistenceChanged;
        MusicPresenceCombo.SelectionChanged += OnMusicPresenceChanged;
        ColorModeCombo.SelectionChanged += OnColorModeChanged;
        IdleLabelCombo.SelectionChanged += OnIdleLabelChanged;
        WallpaperTransitionCombo.SelectionChanged += OnWallpaperTransitionChanged;
        WallpaperRespectToggle.Checked += OnWallpaperRespectChanged;
        WallpaperRespectToggle.Unchecked += OnWallpaperRespectChanged;
        WallpaperAccentToggle.Checked += OnWallpaperAccentChanged;
        WallpaperAccentToggle.Unchecked += OnWallpaperAccentChanged;
        Loaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            UpdateWallpaperAnimationState();
        };
        Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        RetentionBox.LostFocus += OnRetentionLostFocus;
        MaxItemsBox.LostFocus += OnMaxItemsLostFocus;
        LowBatteryBox.LostFocus += OnLowBatteryLostFocus;

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;

    private ConfigService Config => WharfApp.Config!;

    private AppConfig Cfg => Config.Current;

    /// <summary>
    /// <see cref="MonitorManager.Engine"/> throws after Dispose (the settings window can still be open
    /// while the App closes); therefore it can return <see langword="null"/> and the maintenance
    /// buttons become silent no-ops. <see cref="TopBarEngine.Island"/> is always
    /// populated otherwise — the service is created unconditionally in the <c>Engine</c> constructor.
    /// </summary>
    private IslandService? Island
    {
        get
        {
            try
            {
                return WharfApp.Monitors?.Engine.Island;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    private void LoadSettings()
    {
        IslandConfig island = Cfg.Island;
        EnabledToggle.IsChecked = island.Enabled;
        PersistenceCombo.SelectedIndex = IsPersistent(island.Persistence) ? 1 : 0;
        MusicPresenceCombo.SelectedIndex = IslandNormalizer.IsTransientMusic(island.MusicPresence) ? 1 : 0;
        IdleLabelCombo.SelectedIndex = IslandNormalizer.IsLastClipIdle(island.IdleLabel) ? 1 : 0;
        ColorModeCombo.SelectedIndex = IslandNormalizer.NormalizeColorMode(island.ColorMode) switch
        {
            IslandConfig.ColorModeSystem => 1,
            IslandConfig.ColorModeNeutral => 2,
            _ => 0,
        };

        WallpaperTransitionCombo.SelectedIndex = IslandNormalizer.NormalizeWallpaperTransition(island.WallpaperTransition) switch
        {
            IslandConfig.TransitionOff => 0,
            IslandConfig.TransitionFade => 1,
            _ => 2,
        };
        WallpaperRespectToggle.IsChecked = island.WallpaperRespectSystemAnimation;
        WallpaperAccentToggle.IsChecked = island.WallpaperAccent;
        WallpaperAccentTitleText.Text = LocalizationService.Get("Island_WallpaperAccent_Title");
        WallpaperAccentDescriptionText.Text = LocalizationService.Get("Island_WallpaperAccent_Description");
        UpdateWallpaperAnimationState();

        // IslandNormalizer always clamps to 1..3650 / 10..5000; the boxes show the same value
        // (so overflow values written into config.json by hand do not surprise the user).
        _syncingLimits = true;
        RetentionBox.Text = IslandNormalizer.ClampRetentionDays(island.RetentionDays)
            .ToString(CultureInfo.InvariantCulture);
        MaxItemsBox.Text = IslandNormalizer.ClampMaxItems(island.MaxItems)
            .ToString(CultureInfo.InvariantCulture);
        int lowBattery = IslandNormalizer.NormalizeLowBatteryPercent(island.LowBatteryPercent);
        LowBatteryBox.Text = lowBattery.ToString(CultureInfo.InvariantCulture);
        LowBatteryValueText.Text = lowBattery.ToString(CultureInfo.InvariantCulture);
        _syncingLimits = false;

        UpdateLimitsEnabled();
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Island.Enabled = EnabledToggle.IsChecked == true;
        SaveAndApply();
    }

    private void OnPersistenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || PersistenceCombo.SelectedIndex < 0)
        {
            return;
        }

        // Persistence is a string ("session" / "persistent"), not an enum: the selected index
        // goes through the normalization layer and is bound to the canonical constants.
        Cfg.Island.Persistence = PersistenceCombo.SelectedIndex == 1
            ? IslandConfig.PersistencePersistent
            : IslandConfig.PersistenceSession;

        UpdateLimitsEnabled();
        SaveAndApply();
    }

    private void OnMusicPresenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || MusicPresenceCombo.SelectedIndex < 0)
        {
            return;
        }

        Cfg.Island.MusicPresence = MusicPresenceCombo.SelectedIndex == 1
            ? IslandConfig.MusicPresenceTransient
            : IslandConfig.MusicPresencePersistent;
        SaveAndApply();
    }

    private void OnColorModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ColorModeCombo.SelectedIndex < 0)
        {
            return;
        }

        Cfg.Island.ColorMode = ColorModeCombo.SelectedIndex switch
        {
            1 => IslandConfig.ColorModeSystem,
            2 => IslandConfig.ColorModeNeutral,
            _ => IslandConfig.ColorModeAlbum,
        };
        SaveAndApply();
    }

    private void OnIdleLabelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || IdleLabelCombo.SelectedIndex < 0)
        {
            return;
        }

        Cfg.Island.IdleLabel = IdleLabelCombo.SelectedIndex == 1
            ? IslandConfig.IdleLabelLastClip
            : IslandConfig.IdleLabelClipboard;
        SaveAndApply();
    }

    private void OnWallpaperTransitionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || WallpaperTransitionCombo.SelectedIndex < 0)
        {
            return;
        }

        Cfg.Island.WallpaperTransition = WallpaperTransitionCombo.SelectedIndex switch
        {
            0 => IslandConfig.TransitionOff,
            1 => IslandConfig.TransitionFade,
            _ => IslandConfig.TransitionBlurFade,
        };
        SaveAndApply();
    }

    private void OnWallpaperRespectChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Island.WallpaperRespectSystemAnimation = WallpaperRespectToggle.IsChecked == true;
        UpdateWallpaperAnimationState();
        SaveAndApply();
    }

    private void OnWallpaperAccentChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Island.WallpaperAccent = WallpaperAccentToggle.IsChecked == true;
        SaveAndApply();
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(SystemParameters.ClientAreaAnimation))
        {
            Dispatcher.BeginInvoke(new Action(UpdateWallpaperAnimationState));
        }
    }

    /// <summary>Respect on + Windows animations off: transition never runs, so lock the combo and explain.</summary>
    private void UpdateWallpaperAnimationState()
    {
        bool blocked = WallpaperRespectToggle.IsChecked == true && !SystemParameters.ClientAreaAnimation;
        WallpaperAnimationInfoText.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        WallpaperTransitionCombo.IsEnabled = !blocked;
    }

    private void OnRetentionLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _syncingLimits)
        {
            return;
        }

        if (TryCommitLimit(RetentionBox, IslandNormalizer.ClampRetentionDays, RetentionRangeText, out int days))
        {
            Cfg.Island.RetentionDays = days;
            SaveAndApply();
        }
    }

    private void OnMaxItemsLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _syncingLimits)
        {
            return;
        }

        if (TryCommitLimit(MaxItemsBox, IslandNormalizer.ClampMaxItems, MaxItemsRangeText, out int maxItems))
        {
            Cfg.Island.MaxItems = maxItems;
            SaveAndApply();
        }
    }

    /// <summary>
    /// Converts the text typed into the box into the value to be written to config, clamped with <paramref name="clamp"/>;
    /// it writes the clamped form back into the box (so the user sees the limit was exceeded).
    /// For unparsable input it leaves the user's text untouched and only shows an error — a half-typed
    /// number ("36") must not be silently applied as 3.
    /// </summary>
    /// <summary>Same as the range text under the box — the error message must not contradict it.</summary>
    private const string RetentionRangeText = "1–3650";

    private const string MaxItemsRangeText = "10–5000";

    private const string LowBatteryRangeText = "5–50";

    private void OnLowBatteryLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _syncingLimits)
        {
            return;
        }

        if (!int.TryParse(LowBatteryBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            LowBatteryErrorText.Text = string.Format(Strings.IslandPage_RangeError, LowBatteryRangeText);
            LowBatteryErrorText.Visibility = Visibility.Visible;
            return;
        }

        int percent = IslandNormalizer.ClampLowBatteryPercent(parsed);
        LowBatteryErrorText.Visibility = Visibility.Collapsed;
        LowBatteryBox.Text = percent.ToString(CultureInfo.InvariantCulture);
        LowBatteryValueText.Text = percent.ToString(CultureInfo.InvariantCulture);
        Cfg.Island.LowBatteryPercent = percent;
        SaveAndApply();
    }

    private bool TryCommitLimit(TextBox box, Func<int, int> clamp, string rangeText, out int value)
    {
        value = 0;
        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            // int.TryParse also returns false on overflow input ("99999999999" is a valid integer but
            // does not fit in int), so the message must state the box's real range — "enter a valid integer"
            // would contradict the range text under the box.
            LimitsErrorText.Text = string.Format(Strings.IslandPage_RangeError, rangeText);
            LimitsErrorText.Visibility = Visibility.Visible;
            return false;
        }

        value = clamp(parsed);
        LimitsErrorText.Visibility = Visibility.Collapsed;
        box.Text = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// The day limit is applied only in persistent mode (<c>IslandPruner.Prune</c>:
    /// <c>persistent &amp;&amp; retentionDays &gt; 0</c>), so in session mode the box
    /// is locked. The entry-count ceiling works in both modes, so it stays enabled.
    /// </summary>
    private void UpdateLimitsEnabled()
    {
        RetentionBox.IsEnabled = PersistenceCombo.SelectedIndex == 1;
    }

    private void OnOpenDropFolderClick(object sender, RoutedEventArgs e)
    {
        // Same path as IslandStore.DropRoot; ConfigService is the single source of truth.
        string root = Path.Combine(Config.ConfigDirectory, "Drop");
        try
        {
            Directory.CreateDirectory(root);
            Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WharfBar] open drop folder failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Drops the file entries from the list and empties the <c>Drop</c> folder by scanning it. Calling only
    /// <c>Clear</c> would be wrong: that also deletes the clipboard texts, and the user would lose the whole
    /// history when they said "delete the files".
    /// </summary>
    /// <remarks>
    /// The spec defines this action as "(confirmed)". <c>DeleteDroppedFiles</c> empties the <c>Drop</c>
    /// folder by scanning it and is <b>irreversible</b>, so running it without confirmation is a data-
    /// loss rule violation. Confirmation is obtained through <see cref="ConfirmDestructive"/>; if it cannot
    /// be obtained the action is not performed at all (fail-closed).
    /// </remarks>
    private void OnDeleteDroppedFilesClick(object sender, RoutedEventArgs e)
    {
        if (Island is not { } island)
        {
            return;
        }

        if (!ConfirmDestructive(
                Strings.IslandPage_DeleteConfirmTitle,
                Strings.IslandPage_DeleteConfirmMessage))
        {
            return;
        }

        island.DeleteDroppedFiles();
    }

    /// <summary>Empties the list; does not touch the file copies (deliberate, see IslandService.Clear).</summary>
    private void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        if (Island is not { } island)
        {
            return;
        }

        if (!ConfirmDestructive(
                Strings.IslandPage_ClearConfirmTitle,
                Strings.IslandPage_ClearConfirmMessage))
        {
            return;
        }

        island.Clear();
    }

    /// <summary>
    /// Asks for confirmation for an irreversible maintenance action.
    /// </summary>
    /// <remarks>
    /// There was no confirmation pattern in the repository before.
    /// WPF-UI <c>ContentDialog</c> was tried and <b>rejected</b>: measured by reflection — WPF-UI 4.3.0's
    /// <c>ContentDialog</c> has no <c>XamlRoot</c> property, its only public method is
    /// <c>ShowAsync(CancellationToken)</c> and it resolves the host through
    /// <c>ContentDialogHost.GetForWindow(Window)</c>; whereas <c>SettingsWindow</c>'s content is a
    /// <c>NavigationView</c>, not a <c>ContentDialogHost</c>. So a usable path would require changing
    /// the settings window's XAML structurally — and it cannot be verified because the contract
    /// forbids running the app. A wrong WPF-UI control name killing the process with a XAML parse
    /// error is not an accepted risk on this branch; <c>System.Windows.MessageBox</c> (BCL,
    /// verified by the compiler) was used.
    /// </remarks>
    /// <returns>
    /// <c>true</c> if the user confirmed; <c>false</c> on cancel, close, window not found or
    /// failure to show — i.e. if confirmation is not obtained the action is <b>not</b> performed
    /// (fail-closed).
    /// </returns>
    private bool ConfirmDestructive(string title, string message)
    {
        Window? owner = Window.GetWindow(this);
        if (owner is null)
        {
            Debug.WriteLine("[WharfBar] island confirm skipped, no owner window");
            return false;
        }

        try
        {
            return MessageBox.Show(
                       owner,
                       message,
                       title,
                       MessageBoxButton.OKCancel,
                       MessageBoxImage.Warning)
                   == MessageBoxResult.OK;
        }
        catch (Exception ex)
        {
            // The confirmation could not be shown: the irreversible action is not performed, but the reason must not be invisible.
            Debug.WriteLine($"[WharfBar] island confirm failed: {ex.Message}");
            return false;
        }
    }

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        Island?.ApplyConfig(Cfg.Island);
    }

    private static bool IsPersistent(string? persistence) =>
        string.Equals(
            IslandNormalizer.NormalizePersistence(persistence),
            IslandConfig.PersistencePersistent,
            StringComparison.Ordinal);
}
