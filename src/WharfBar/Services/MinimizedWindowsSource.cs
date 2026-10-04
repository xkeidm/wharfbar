using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>One top-level window, captured during the off-thread scan.</summary>
internal sealed record AppWindowInfo(IntPtr Handle, string Title, uint Pid, bool IsMinimized);

/// <summary>
/// Maintains the full top-level window table off the UI thread and derives the
/// minimized subset the dock shows as its own section.
/// <para>
/// The whole table is cached so the dock's "Pencereler" submenu can filter it on the UI
/// thread without calling <c>EnumWindows</c> (which stalls on a hung HWND).
/// </para>
/// </summary>
internal sealed class MinimizedWindowsSource : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(800);

    private readonly Dispatcher _dispatcher;
    private List<AppWindowInfo> _windows = [];
    private List<AppWindowInfo> _minimized = [];
    private DispatcherTimer? _poll;
    private bool _disposed;
    private int _refreshBusy;

    internal MinimizedWindowsSource(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => RequestRefresh();
        _poll.Start();
        RequestRefresh();
    }

    internal IReadOnlyList<AppWindowInfo> Windows => _windows;

    internal IReadOnlyList<AppWindowInfo> Minimized => _minimized;

    internal event EventHandler? Changed;

    internal void Refresh() => RequestRefresh();

    internal static IReadOnlyList<AppWindowInfo> SelectMinimized(IReadOnlyList<AppWindowInfo> windows) =>
        windows is null ? [] : [.. windows.Where(static w => w.IsMinimized)];

    /// <summary>
    /// Every window owned by <paramref name="pid"/>, visible first so the dock's menu
    /// shows the main window above the minimized ones.
    /// </summary>
    internal static IReadOnlyList<AppWindowInfo> ForProcess(IReadOnlyList<AppWindowInfo> windows, uint pid)
    {
        if (windows is null || pid == 0)
        {
            return [];
        }

        return [.. windows.Where(w => w.Pid == pid).OrderBy(static w => w.IsMinimized)];
    }

    private void RequestRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshBusy, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                List<AppWindowInfo> next = Scan();
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
            finally
            {
                Interlocked.Exchange(ref _refreshBusy, 0);
            }
        });
    }

    private static List<AppWindowInfo> Scan()
    {
        var all = new List<AppWindowInfo>();

        foreach (var (hwnd, title, pid) in WindowEnumeration.GetTopLevelCandidates())
        {
            all.Add(new AppWindowInfo(hwnd, title, pid, NativeMethods.IsIconic(hwnd)));
        }

        return all;
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
        _poll?.Stop();
        _poll = null;
    }
}
