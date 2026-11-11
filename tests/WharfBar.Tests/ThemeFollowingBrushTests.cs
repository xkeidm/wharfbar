using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Services;
using WharfBar.Views;
using WharfBar.Views.Controls;
using Wpf.Ui.Appearance;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The dock's running / minimized indicator and progress lane must follow the light/dark theme at
/// runtime. They used to freeze the brush they read on the first paint (<c>TryFindResource</c>
/// assigns a snapshot), so switching from dark to light left the short "minimized" line in the
/// dark-theme colour — the light theme paints that lane with a near-invisible light line. WPF only
/// re-resolves <c>SetResourceReference</c> values when the dictionary that supplied them is
/// swapped; this test swaps the dictionary and reads the lane again without repainting the cell.
///
/// The theme dictionary lives on the test window (not <c>Application.Current</c>) on purpose: the
/// lookup/invalidation path is the same one the bar-level palette uses, and the test stays
/// independent of any other WPF test's Application.
/// </summary>
public class ThemeFollowingBrushTests
{
    private static readonly Color LightText = Color.FromArgb(0xE4, 0x00, 0x00, 0x00);
    private static readonly Color DarkText = Colors.White;
    private static readonly Color LightAccent = Color.FromRgb(0x00, 0x5F, 0xB8);
    private static readonly Color DarkAccent = Color.FromRgb(0x60, 0xCD, 0xFF);

    [Fact]
    public void DockRunningPillAndProgressLane_FollowTheDictionarySwapWithoutARepaint()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var cell = new DockItemCell { MotionEnabled = false };
                cell.ApplyMetrics(glyphSize: 24d, buttonSize: 40d, height: 40d);

                window = new Window
                {
                    Content = cell,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Left = -32000,
                    Top = -32000,
                    Width = 200,
                    Height = 80,
                };
                window.Resources.MergedDictionaries.Add(Theme(LightText, LightAccent));
                window.Show();

                // After the tree exists, so the brush is resolved through the window dictionary —
                // the same order the dock builds its cells in.
                cell.Row = Item(running: true, openOnDesktop: false);
                cell.ApplyProgress(DockProgressState.Normal(40));

                Assert.Equal(LightText, ResolvedColor(cell.RunningPill.Background));
                Assert.Equal(LightAccent, ResolvedColor(cell.ProgressFill.Background));

                // Same keys, opposite palette: exactly what WPF-UI does on a light/dark swap.
                window.Resources.MergedDictionaries[0] = Theme(DarkText, DarkAccent);

                Assert.Equal(DarkText, ResolvedColor(cell.RunningPill.Background));
                Assert.Equal(DarkAccent, ResolvedColor(cell.ProgressFill.Background));
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                window?.Close();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }

    [Fact]
    public void PinnedThemePreference_IsWhatTheAppResolves()
    {
        string original = AppThemePreference.Mode;
        try
        {
            AppThemePreference.Mode = AppConfig.ThemeLight;
            Assert.Equal(ApplicationTheme.Light, AppThemePreference.Resolve());

            AppThemePreference.Mode = AppConfig.ThemeDark;
            Assert.Equal(ApplicationTheme.Dark, AppThemePreference.Resolve());

            // Unknown values fall back to "follow the system", never to a half-applied theme.
            AppThemePreference.Mode = "sepia";
            Assert.Equal(AppConfig.ThemeSystem, AppThemePreference.Mode);
        }
        finally
        {
            AppThemePreference.Mode = original;
        }
    }

    private static ResourceDictionary Theme(Color text, Color accent) => new()
    {
        ["TextFillColorPrimaryBrush"] = Frozen(text),
        ["AccentFillColorDefaultBrush"] = Frozen(accent),
    };

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ResolvedColor(Brush? brush) => brush is SolidColorBrush solid
        ? solid.Color
        : throw new Xunit.Sdk.XunitException($"expected a theme SolidColorBrush, got {brush?.GetType().Name ?? "null"}");

    private static DockItemViewModel Item(bool running, bool openOnDesktop = false) =>
        DockItemViewModel.ForItem(
            new DockAppItem(
                "id",
                @"C:\Windows\System32\notepad.exe",
                string.Empty,
                "Notepad",
                IsPinned: true,
                IsRunning: running,
                IntPtr.Zero,
                DockSection.Pinned,
                string.Empty,
                IsOpenOnDesktop: openOnDesktop,
                WindowCount: 0),
            null!);
}
