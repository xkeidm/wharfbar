using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WharfBar.Native;

/// <summary>
/// TEMPORARY diagnostic — remove after the tray-peek flash investigation.
/// Appends peek/click-time taskbar state to %TEMP%\wharfbar-tray-peek-diag.log so the
/// user can run the app briefly and share the file to trace shell interactions.
/// </summary>
internal static class TrayPeekDiagnostics
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "wharfbar-tray-peek-diag.log");

    internal static void Log(string phase, IntPtr trayHwnd, bool cloakResult)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {phase}");
            sb.AppendLine($"  tray hwnd={trayHwnd} visible={NativeMethods.IsWindowVisible(trayHwnd)}");
            if (trayHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(trayHwnd, out RECT trayRect))
            {
                sb.AppendLine($"  tray rect={trayRect.Left},{trayRect.Top},{trayRect.Right},{trayRect.Bottom} (w={trayRect.Width} h={trayRect.Height})");
            }

            sb.AppendLine($"  cloak={cloakResult}");
            sb.AppendLine("  visible windows intersecting primary bottom strip:");

            IntPtr primary = NativeMethods.MonitorFromWindow(trayHwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (primary != IntPtr.Zero && NativeMethods.GetMonitorInfo(primary, ref mi))
            {
                int stripTop = mi.rcMonitor.Bottom - 80;
                NativeMethods.EnumWindows((h, _) =>
                {
                    if (NativeMethods.IsWindowVisible(h)
                        && NativeMethods.GetWindowRect(h, out RECT r)
                        && r.Bottom > stripTop
                        && r.Top < mi.rcMonitor.Bottom
                        && r.Right > mi.rcMonitor.Left
                        && r.Left < mi.rcMonitor.Right)
                    {
                        var cls = new StringBuilder(256);
                        var title = new StringBuilder(256);
                        _ = NativeMethods.GetClassName(h, cls, cls.Capacity);
                        _ = NativeMethods.GetWindowText(h, title, title.Capacity);
                        sb.AppendLine($"    {h} class='{cls}' title='{title}' rect={r.Left},{r.Top},{r.Right},{r.Bottom}");
                    }

                    return true;
                }, IntPtr.Zero);
            }

            lock (Gate)
            {
                File.AppendAllText(LogPath, sb.ToString());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar tray-peek diag failed: {ex.Message}");
        }
    }

    /// <summary>Appends a single free-form line (click-chain tracing).</summary>
    internal static void LogNote(string line)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar tray-peek diag note failed: {ex.Message}");
        }
    }
}
