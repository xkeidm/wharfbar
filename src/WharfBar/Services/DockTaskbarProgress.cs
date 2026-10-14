using System.Diagnostics;
using System.Windows.Automation;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Reads taskbar-button progress from <c>Shell_TrayWnd</c> only, not the desktop root.
/// There is no documented <c>GetProgressValue</c>. When the parked taskbar exposes no
/// progress pattern, every tile stays <see cref="DockProgressState.None"/>.
/// </summary>
internal static class DockTaskbarProgress
{
    private const string TaskListButtonClass = "Taskbar.TaskListButtonAutomationPeer";
    private const string TrayClass = "Shell_TrayWnd";
    private const string SecondaryTrayClass = "Shell_SecondaryTrayWnd";

    internal readonly record struct Scan(bool Exposed, IReadOnlyList<DockProgressButton> Buttons);

    internal static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan QuietInterval = TimeSpan.FromSeconds(20);

    /// <summary>UI Automation walks only while a running app's taskbar button is actually exposed.</summary>
    internal static TimeSpan NextInterval(bool anyRunning, bool trayExposed)
        => anyRunning && trayExposed ? ActiveInterval : QuietInterval;

    /// <summary>
    /// Reads taskbar buttons under <c>Shell_TrayWnd</c> only. A desktop-root walk hits every
    /// window on the machine and dumps a stream of automation faults into the debugger on startup.
    /// A parked tray that exposes no buttons stays <see cref="Scan.Exposed"/> false.
    /// </summary>
    internal static Scan ReadButtons()
    {
        var buttons = new List<DockProgressButton>();
        bool exposed = false;
        foreach (IntPtr hwnd in EnumerateTrayWindows())
        {
            AutomationElementCollection? found;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                found = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ClassNameProperty, TaskListButtonClass));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar taskbar progress scan failed: {ex.Message}");
                continue;
            }

            if (found is null || found.Count == 0)
            {
                continue;
            }

            exposed = true;
            foreach (AutomationElement element in found)
            {
                string name;
                try
                {
                    name = element.Current.Name ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WharfBar taskbar button name failed: {ex.Message}");
                    continue;
                }

                if (!TryReadSample(element, out DockProgressSample sample))
                {
                    continue;
                }

                DockProgressState state = DockProgress.FromSample(sample);
                if (state.IsVisible)
                {
                    buttons.Add(new DockProgressButton(name, state));
                }
            }
        }

        return new Scan(exposed, buttons);
    }

    private static IEnumerable<IntPtr> EnumerateTrayWindows()
    {
        IntPtr primary = NativeMethods.FindWindow(TrayClass, null);
        if (primary != IntPtr.Zero)
        {
            yield return primary;
        }

        IntPtr secondary = IntPtr.Zero;
        while (true)
        {
            secondary = NativeMethods.FindWindowEx(IntPtr.Zero, secondary, SecondaryTrayClass, null);
            if (secondary == IntPtr.Zero)
            {
                yield break;
            }

            yield return secondary;
        }
    }

    internal static IReadOnlyDictionary<string, DockProgressState> ForItems(
        IReadOnlyList<DockAppItem> items,
        IReadOnlyList<AppWindowInfo> windows)
        => Assign(items, windows, ReadButtons());

    internal static IReadOnlyDictionary<string, DockProgressState> Assign(
        IReadOnlyList<DockAppItem> items,
        IReadOnlyList<AppWindowInfo> windows,
        Scan scan)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!scan.Exposed)
        {
            return new Dictionary<string, DockProgressState>();
        }

        var tiles = new List<DockProgressTile>();
        foreach (DockAppItem item in items)
        {
            if (item.Kind != DockTileKind.App || !item.IsRunning)
            {
                continue;
            }

            string title = TitleFor(item, windows);
            tiles.Add(new DockProgressTile(DockPresence.KeyFor(item), title, item.Label));
        }

        return DockProgress.Assign(tiles, scan.Buttons);
    }

    private static string TitleFor(DockAppItem item, IReadOnlyList<AppWindowInfo>? windows)
    {
        if (windows is null)
        {
            return item.Label;
        }

        for (int i = 0; i < windows.Count; i++)
        {
            AppWindowInfo window = windows[i];
            if (window.Handle == item.MainWindowHandle && !string.IsNullOrWhiteSpace(window.Title))
            {
                return window.Title;
            }
        }

        if (item.ProcessIds is null)
        {
            return item.Label;
        }

        for (int i = 0; i < windows.Count; i++)
        {
            AppWindowInfo window = windows[i];
            if (string.IsNullOrWhiteSpace(window.Title) || window.Pid == 0)
            {
                continue;
            }

            for (int p = 0; p < item.ProcessIds.Count; p++)
            {
                if (item.ProcessIds[p] == window.Pid)
                {
                    return window.Title;
                }
            }
        }

        return item.Label;
    }

    private static bool TryReadSample(AutomationElement button, out DockProgressSample sample)
    {
        sample = default;
        try
        {
            AutomationElement? progress = button.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ProgressBar));
            AutomationElement source = progress ?? button;

            string? status = source.Current.ItemStatus;
            if (string.IsNullOrWhiteSpace(status))
            {
                status = button.Current.ItemStatus;
            }

            DockProgressFlags flags = DockProgress.FlagsFromText(status);
            flags |= DockProgress.FlagsFromText(source.Current.HelpText);

            if (source.TryGetCurrentPattern(RangeValuePattern.Pattern, out object? pattern)
                && pattern is RangeValuePattern range)
            {
                var info = range.Current;
                sample = new DockProgressSample(true, info.Minimum, info.Maximum, info.Value, flags);
                return true;
            }

            if (flags != DockProgressFlags.None)
            {
                sample = new DockProgressSample(true, null, null, null, flags);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar taskbar progress read failed: {ex.Message}");
        }

        return false;
    }
}
