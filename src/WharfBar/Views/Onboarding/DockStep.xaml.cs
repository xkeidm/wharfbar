using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views.Onboarding;

/// <summary>
/// Setup step 4: dock position, icon size, background and behavior. Every change is saved and
/// applied straight away, so the dock on the real desktop moves and restyles behind the wizard.
/// </summary>
public partial class DockStep : UserControl
{
    private const string TileStyleKey = "PositionTile";
    private const string TileSelectedStyleKey = "PositionTileSelected";

    /// <summary>Guards the XAML-declared handlers while the ctor pushes the saved config into them.</summary>
    private bool _isLoading = true;

    public DockStep()
    {
        InitializeComponent();
        LoadState();
        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    // --- Load ---

    private void LoadState()
    {
        UpdatePositionTiles(Cfg.Dock.Position);

        IconSizeSlider.Value = Math.Clamp(Cfg.Dock.IconSize, DockConfig.MinIconSize, DockConfig.MaxIconSize);

        AutoHideToggle.IsChecked = Cfg.Dock.AutoHide;
        ShowRunningToggle.IsChecked = Cfg.Dock.ShowRunningApps;

        string mode = TopBarBackgroundModes.NormalizeMode(Cfg.Dock.BackgroundMode);
        BackgroundCombo.SelectedIndex = mode switch
        {
            TopBarBackgroundModes.Transparent => 1,
            TopBarBackgroundModes.Tint => 2,
            _ => 0,
        };
        OpacitySlider.Value = TopBarBackgroundModes.ClampOpacity(Cfg.Dock.TintOpacity);
        OpacityLabel.Text = $"{TopBarBackgroundModes.ClampOpacity(Cfg.Dock.TintOpacity)}%";
        UpdateOpacityRow(mode);
    }

    // --- Position ---

    private void OnPositionTileClick(object sender, MouseButtonEventArgs e)
    {
        if (_isLoading || sender is not Border { Tag: string position })
        {
            return;
        }

        string normalized = DockPositions.Normalize(position);
        if (!string.Equals(DockPositions.Normalize(Cfg.Dock.Position), normalized, StringComparison.Ordinal))
        {
            Cfg.Dock.Position = normalized;
            SaveAndApplyDock();
        }

        UpdatePositionTiles(normalized);
    }

    /// <summary>Accent ring on the tile that matches the saved anchor, plain card on the others.</summary>
    private void UpdatePositionTiles(string position)
    {
        string normalized = DockPositions.Normalize(position);
        SetTileSelected(PositionCenterTile, normalized == DockPositions.BottomCenter);
        SetTileSelected(PositionLeftEdgeTile, normalized == DockPositions.Left);
    }

    private void SetTileSelected(Border tile, bool selected)
    {
        tile.Style = (Style)FindResource(selected ? TileSelectedStyleKey : TileStyleKey);
    }

    // --- Icon size ---

    private void OnIconSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateIconSizeLabel(e.NewValue);

        if (_isLoading)
        {
            return;
        }

        // SnapToTick keeps the slider whole, but the bound value is authoritative.
        Cfg.Dock.IconSize = Math.Clamp(
            Math.Round(e.NewValue),
            DockConfig.MinIconSize,
            DockConfig.MaxIconSize);
        SaveAndApplyDock();
    }

    private void UpdateIconSizeLabel(double value)
    {
        // The slider raises ValueChanged while XAML is still building the row: the label field is
        // assigned later in the parse, so the first call must be a no-op.
        if (IconSizeLabel is null)
        {
            return;
        }

        int size = (int)Math.Round(value);
        IconSizeLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Onboarding_Dock_SizeFormat,
            size);
    }

    // --- Background ---

    private void OnBackgroundModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        string newMode = BackgroundCombo.SelectedIndex switch
        {
            1 => TopBarBackgroundModes.Transparent,
            2 => TopBarBackgroundModes.Tint,
            _ => TopBarBackgroundModes.Mica,
        };

        string oldMode = TopBarBackgroundModes.NormalizeMode(Cfg.Dock.BackgroundMode);
        bool oldNeedsTx = TopBarBackgroundModes.NeedsTransparency(oldMode);
        bool newNeedsTx = TopBarBackgroundModes.NeedsTransparency(newMode);

        Cfg.Dock.BackgroundMode = newMode;
        Config.Save(Cfg);
        UpdateOpacityRow(newMode);

        // A mode that adds/removes WS_EX_LAYERED cannot be reached by a repaint: rebuild the dock.
        if (oldNeedsTx != newNeedsTx)
        {
            WharfApp.RecreateDock();
        }
        else
        {
            WharfApp.ApplyDockChanges();
        }
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int value = (int)Math.Round(e.NewValue);
        if (OpacityLabel is not null)
        {
            // Fires once during XAML parse, before the label field exists.
            OpacityLabel.Text = $"{value}%";
        }

        if (_isLoading)
        {
            return;
        }

        Cfg.Dock.TintOpacity = TopBarBackgroundModes.ClampOpacity(value);
        SaveAndApplyDock();
    }

    private void UpdateOpacityRow(string mode)
    {
        OpacityRow.Visibility = mode == TopBarBackgroundModes.Tint
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // --- Behavior ---

    private void OnAutoHideChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Dock.AutoHide = AutoHideToggle.IsChecked == true;
        SaveAndApplyDock();
    }

    private void OnShowRunningChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Dock.ShowRunningApps = ShowRunningToggle.IsChecked == true;
        SaveAndApplyDock();
    }

    private void SaveAndApplyDock()
    {
        Config.Save(Cfg);
        WharfApp.ApplyDockChanges();
    }
}
