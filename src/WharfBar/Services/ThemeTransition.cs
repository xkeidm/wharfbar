using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Views;

namespace WharfBar.Services;

/// <summary>
/// Crossfades the shell from the old light/dark palette to the new one.
///
/// A theme switch is a synchronous WPF-UI resource-dictionary swap: every themed property in every
/// window is re-resolved in one dispatcher turn, and the shell then re-asserts each surface. Doing
/// nothing shows a hard flip (and, before <see cref="ThemeApplyGuard"/>, the library's main-window
/// repaint could flash an opaque bar). Instead:
///
/// 1. every registered shell window's content is captured to a bitmap at its current DPI;
/// 2. overlay windows showing those frames are put above the shell;
/// 3. the theme is applied and all surfaces re-assert underneath;
/// 4. the old frame fades out over <see cref="ThemeTransitionPolicy.CrossfadeMs"/>.
///
/// The captured frame covers the swap, so the blocking apply is never seen as a frozen frame and
/// the palette change reads as a crossfade. Windows with client-area animations disabled skip the
/// whole thing and apply instantly, like every other motion in the app.
/// </summary>
internal static class ThemeTransition
{
    private static readonly List<ThemeCrossfadeOverlayWindow> ActiveOverlays = [];
    private static DateTime _suppressReactiveUntilUtc = DateTime.MinValue;
    private static int _generation;

    /// <summary>True while a fade is on screen or the post-transition suppression window is open.</summary>
    internal static bool IsActive =>
        ActiveOverlays.Count > 0 || DateTime.UtcNow < _suppressReactiveUntilUtc;

    /// <summary>
    /// True while the Windows broadcast that follows a theme write must not trigger per-window
    /// re-applies: the switch already re-asserted every surface and a second wave mid-fade is the
    /// stutter this transition exists to remove.
    /// </summary>
    internal static bool IsSuppressingReactiveRefresh => DateTime.UtcNow < _suppressReactiveUntilUtc;

    /// <summary>
    /// Runs <paramref name="applyTheme"/> between the capture and the fade. The action must be
    /// synchronous and must leave every shell surface in its final state.
    /// </summary>
    /// <param name="applyTheme">The palette swap plus every shell re-assert.</param>
    /// <param name="afterFade">
    /// Optional follow-up that should only start once the crossfade has finished — e.g. the
    /// wallpaper theme-partner switch, which runs its own fade and would otherwise overlap this
    /// one. Runs immediately when no animation is used; skipped when the apply failed or a newer
    /// transition superseded this one.
    /// </param>
    internal static void Run(Action applyTheme, Action? afterFade = null)
    {
        ArgumentNullException.ThrowIfNull(applyTheme);

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Run(applyTheme, afterFade));
            return;
        }

        // A second switch while the first fades: drop the stale frames, capture from the state that
        // is actually on screen now, and fade again from there.
        CloseOverlays();

        bool animate = ThemeTransitionPolicy.ShouldAnimate(SystemParameters.ClientAreaAnimation);
        List<ThemeCrossfadeOverlayWindow> overlays = animate ? CaptureShell() : [];
        int generation = ++_generation;
        bool applied = false;

        try
        {
            applyTheme();
            applied = true;
        }
        finally
        {
            _suppressReactiveUntilUtc = DateTime.UtcNow + ThemeTransitionPolicy.ReactiveSuppression;

            if (!applied)
            {
                // The apply threw: never leave a stale frame on screen covering the error state.
                CloseAll(overlays);
            }
            else if (generation == _generation)
            {
                StartFade(overlays, afterFade);
            }
            else
            {
                CloseAll(overlays);
            }
        }
    }

    /// <summary>Captures every registered, visible shell window. Individual failures are skipped.</summary>
    private static List<ThemeCrossfadeOverlayWindow> CaptureShell()
    {
        var overlays = new List<ThemeCrossfadeOverlayWindow>();
        if (Application.Current is null)
        {
            return overlays;
        }

        foreach ((Window window, FrameworkElement visual) in ThemeCrossfadeRegistry.CaptureTargets())
        {
            ThemeCrossfadeOverlayWindow? overlay = TryCapture(window, visual);
            if (overlay is not null)
            {
                overlays.Add(overlay);
            }
        }

        return overlays;
    }

    private static ThemeCrossfadeOverlayWindow? TryCapture(Window window, FrameworkElement visual)
    {
        try
        {
            if (!ThemeTransitionPolicy.IsEligible(
                    window.IsLoaded,
                    window.Visibility == Visibility.Visible && visual.IsVisible,
                    window.WindowState == WindowState.Minimized,
                    visual.ActualWidth,
                    visual.ActualHeight))
            {
                return null;
            }

            PresentationSource? source = PresentationSource.FromVisual(visual);
            if (source?.CompositionTarget is null)
            {
                return null;
            }

            Matrix toDevice = source.CompositionTarget.TransformToDevice;
            double dpiScale = toDevice.M11 > 0 ? toDevice.M11 : 1;
            double scale = ThemeTransitionPolicy.SnapshotScale(visual.ActualWidth, visual.ActualHeight, dpiScale);
            int pixelWidth = Math.Max(1, (int)Math.Round(visual.ActualWidth * dpiScale * scale));
            int pixelHeight = Math.Max(1, (int)Math.Round(visual.ActualHeight * dpiScale * scale));

            // The overlay must cover the whole on-screen surface even when the bitmap was
            // downscaled to stay inside the pixel budget; the image is stretched over this rect.
            int surfaceWidth = Math.Max(1, (int)Math.Round(visual.ActualWidth * dpiScale));
            int surfaceHeight = Math.Max(1, (int)Math.Round(visual.ActualHeight * dpiScale));

            Point origin = visual.PointToScreen(new Point(0, 0));
            var physicalRect = new Rect(origin.X, origin.Y, surfaceWidth, surfaceHeight);

            // The auto-hidden dock lives just off-screen; an overlay there would never be seen and
            // could show up on another monitor's edge. Capture only content that is really on screen.
            if (!IntersectsAnyMonitor(physicalRect))
            {
                return null;
            }

            var bitmap = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96 * dpiScale * scale,
                96 * dpiScale * scale,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();

            var overlay = new ThemeCrossfadeOverlayWindow(bitmap, physicalRect, dpiScale);
            overlay.Show();
            return overlay;
        }
        catch (Exception)
        {
            // A window that cannot be snapshotted simply switches instantly; the transition itself
            // must never fail because of one surface (e.g. closing while the capture runs).
            return null;
        }
    }

    /// <summary>True when any part of <paramref name="physicalRect"/> lands on a monitor.</summary>
    private static bool IntersectsAnyMonitor(Rect physicalRect)
    {
        int left = (int)Math.Round(physicalRect.Left);
        int top = (int)Math.Round(physicalRect.Top);
        int right = (int)Math.Round(physicalRect.Right);
        int bottom = (int)Math.Round(physicalRect.Bottom);

        foreach (MonitorInfo monitor in MonitorNative.Enumerate())
        {
            RECT bounds = monitor.Bounds;
            if (left < bounds.Right && right > bounds.Left && top < bounds.Bottom && bottom > bounds.Top)
            {
                return true;
            }
        }

        return false;
    }

    private static void StartFade(List<ThemeCrossfadeOverlayWindow> overlays, Action? afterFade)
    {
        if (overlays.Count == 0)
        {
            afterFade?.Invoke();
            return;
        }

        int remaining = overlays.Count;
        foreach (ThemeCrossfadeOverlayWindow overlay in overlays)
        {
            ActiveOverlays.Add(overlay);
            overlay.FadeOut(() =>
            {
                ActiveOverlays.Remove(overlay);
                if (--remaining == 0)
                {
                    afterFade?.Invoke();
                }
            });
        }
    }

    private static void CloseOverlays()
    {
        foreach (ThemeCrossfadeOverlayWindow overlay in ActiveOverlays.ToList())
        {
            Close(overlay);
        }

        ActiveOverlays.Clear();
    }

    private static void CloseAll(List<ThemeCrossfadeOverlayWindow> overlays)
    {
        foreach (ThemeCrossfadeOverlayWindow overlay in overlays)
        {
            Close(overlay);
        }
    }

    private static void Close(ThemeCrossfadeOverlayWindow overlay)
    {
        try
        {
            overlay.Cancel();
        }
        catch (InvalidOperationException)
        {
            // Already closing/closed (app shutdown).
        }
    }
}
