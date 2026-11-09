using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;

namespace WharfBar.Services;

/// <summary>One top-level window, captured during the off-thread scan.</summary>
internal sealed record AppWindowInfo(IntPtr Handle, string Title, uint Pid, bool IsMinimized);

/// <summary>
/// Maintains the full top-level window table off the UI thread and derives the
/// minimized subset the dock shows as its own section.
/// <para>
/// The whole table is cached so the dock's "Pencereler" submenu can filter it on the UI
/// thread without calling <c>EnumWindows</c> (which stalls on a hung HWND).
/// The table comes from the shared <see cref="WindowCensus"/>, not a private timer.
/// </para>
/// </summary>
internal sealed class MinimizedWindowsSource : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly WindowCensus _census;
    private List<AppWindowInfo> _windows = [];
    private List<AppWindowInfo> _minimized = [];
    private bool _disposed;

    internal MinimizedWindowsSource(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _census = WindowCensus.Acquire();
        _census.Subscribe(OnCensus);
    }

    internal IReadOnlyList<AppWindowInfo> Windows => _windows;

    internal IReadOnlyList<AppWindowInfo> Minimized => _minimized;

    internal event EventHandler? Changed;

    internal void Refresh() => _census.RequestScan();

    internal static IReadOnlyList<AppWindowInfo> SelectMinimized(IReadOnlyList<AppWindowInfo> windows) =>
        windows is null ? [] : [.. windows.Where(static w => w.IsMinimized)];

    private void OnCensus(IReadOnlyList<CensusWindow> windows)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var next = new List<AppWindowInfo>(windows.Count);
            foreach (CensusWindow window in windows)
            {
                next.Add(new AppWindowInfo(window.Hwnd, window.Title, window.Pid, window.IsMinimized));
            }

            if (_disposed)
            {
                return;
            }

            _ = _dispatcher.BeginInvoke(() => ApplyScan(next));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar minimized-windows scan failed: {ex.Message}");
        }
    }

    private void ApplyScan(List<AppWindowInfo> next)
    {
        if (_disposed)
        {
            return;
        }

        if (WindowsEqual(_windows, next))
        {
            return;
        }

        // Both lists come from one snapshot and are assigned together, so a Changed
        // subscriber that rebuilds the dock never reads a stale Minimized.
        _windows = next;
        _minimized = [.. SelectMinimized(next)];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Field-wise window-table comparison. A window-title change counts as a change: the dock's
    /// tooltips show it, so <c>Changed</c> has to fire.
    /// </summary>
    internal static bool WindowsEqual(List<AppWindowInfo> a, List<AppWindowInfo> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Handle != b[i].Handle
                || a[i].Pid != b[i].Pid
                || a[i].IsMinimized != b[i].IsMinimized
                || !string.Equals(a[i].Title, b[i].Title, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _census.Unsubscribe(OnCensus);
        _census.Release();
    }
}
