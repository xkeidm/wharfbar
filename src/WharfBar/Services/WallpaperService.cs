using System.IO;
using System.Windows.Media.Imaging;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Views;

namespace WharfBar.Services;

internal static class WallpaperPaths
{
    internal static string BundledRoot =>
        Path.Combine(AppContext.BaseDirectory, "Wallpapers");

    internal static string UserRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WharfBar",
            "wallpapers");
}

/// <summary>Live transition settings from the primary top bar.</summary>
internal readonly record struct WallpaperTransitionRequest(
    string Mode,
    bool RespectSystemAnimation,
    bool ClientAreaAnimation,
    int MonitorWidthPx);

internal sealed class WallpaperService
{
    internal WallpaperService(string bundledRoot, string userRoot)
    {
        BundledRoot = bundledRoot;
        UserRoot = userRoot;
    }

    internal string BundledRoot { get; }

    internal string UserRoot { get; }

    internal void EnsureUserRoot() => Directory.CreateDirectory(UserRoot);

    /// <summary>Supplies live transition settings; set by the primary top bar.</summary>
    internal Func<WallpaperTransitionRequest?>? TransitionRequest { get; set; }

    internal Func<bool>? AccentEnabled { get; set; }

    private WallpaperTransitionWindow? _overlay;

    internal bool IsAllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!File.Exists(full) || !WallpaperCatalog.IsImageExtension(full))
        {
            return false;
        }

        return Under(BundledRoot, full) || Under(UserRoot, full);
    }

    internal bool TryApply(string path)
    {
        if (!IsAllowedPath(path))
        {
            return false;
        }

        string full = Path.GetFullPath(path);
        string? previous = TryGetDesktopWallpaperPath();

        WallpaperTransitionRequest? request = null;
        BitmapSource? fromBmp = null;
        BitmapSource? toBmp = null;
        try
        {
            if (TransitionRequest?.Invoke() is { } r
                && previous is not null
                && !string.Equals(previous, full, StringComparison.OrdinalIgnoreCase)
                && WallpaperTransitionPolicy.ShouldAnimate(r.Mode, r.RespectSystemAnimation, r.ClientAreaAnimation)
                && r.MonitorWidthPx > 0)
            {
                fromBmp = WallpaperTransitionWindow.TryDecode(previous, r.MonitorWidthPx);
                toBmp = WallpaperTransitionWindow.TryDecode(full, r.MonitorWidthPx);
                if (fromBmp is not null && toBmp is not null)
                {
                    request = r;
                }
            }
        }
        catch (Exception)
        {
            fromBmp = null;
            toBmp = null;
            request = null;
        }

        // Prefer animated crossfade behind icons; SPI commits when the fade finishes.
        if (request is { } req && fromBmp is not null && toBmp is not null)
        {
            try
            {
                if (BeginTransition(fromBmp, toBmp, WallpaperTransitionPolicy.UsesBlur(req.Mode), full))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                CancelTransition();
            }
        }

        // Instant path (policy off, WorkerW missing, or decode failed).
        if (DesktopWallpaper.TrySet(full))
        {
            TrySyncAccent(full);
            return true;
        }

        return false;
    }

    private void TrySyncAccent(string fullPath)
    {
        try
        {
            bool on = AccentEnabled?.Invoke() ?? IslandConfig.DefaultWallpaperAccent;
            _ = WallpaperAccentApplier.TryApplyFromPath(fullPath, on);
        }
        catch (Exception)
        {
            // ignore
        }
    }

    internal void CancelTransition()
    {
        WallpaperTransitionWindow? w = _overlay;
        _overlay = null;
        w?.Cancel();
    }

    /// <summary>
    /// Starts the WorkerW-hosted crossfade. On success, SPI runs when the animation ends.
    /// Returns false if the host could not be attached (caller should SPI immediately).
    /// </summary>
    private bool BeginTransition(BitmapSource from, BitmapSource to, bool useBlur, string applyPath)
    {
        CancelTransition();
        var window = new WallpaperTransitionWindow();
        _overlay = window;
        bool started = window.TryPlay(from, to, useBlur, completed: () =>
        {
            try
            {
                if (DesktopWallpaper.TrySet(applyPath))
                {
                    TrySyncAccent(applyPath);
                }
            }
            finally
            {
                if (ReferenceEquals(_overlay, window))
                {
                    _overlay = null;
                }
            }
        });

        if (!started)
        {
            _overlay = null;
            try
            {
                window.Close();
            }
            catch (InvalidOperationException)
            {
                // ignore
            }
        }

        return started;
    }

    internal string? TryGetCurrentPath()
    {
        string? desktop = TryGetDesktopWallpaperPath();
        if (desktop is null || !IsAllowedPath(desktop))
        {
            return null;
        }

        return desktop;
    }

    /// <summary>Current SPI wallpaper path when it points at an existing image file (any location).</summary>
    internal string? TryGetDesktopWallpaperPath()
    {
        if (!DesktopWallpaper.TryGet(out string path) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string full = Path.GetFullPath(path);
            return File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool Under(string root, string full)
    {
        string prefix = Path.GetFullPath(root)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
