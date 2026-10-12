using System.Diagnostics;
using System.Windows.Automation;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// "Her zaman enerji tasarrufu kullan" anahtarını UI Automation ile çevirir.
/// Belgelenmiş bir yazma API'si yok; Ayarlar penceresi ekran dışına alınır ve
/// bu çağrı açtıysa kapanır. Anahtarın yeni hali geri okunur.
/// </summary>
internal static class EnergySaverAlwaysOn
{
    private const string ToggleId = "SystemSettings_PowerAndBattery_EnergySaverAlwaysOn_ToggleSwitch";
    private const string ExpandId = "EntityItemButton";

    internal static bool? TrySet(bool on)
    {
        bool launched = Process.GetProcessesByName("SystemSettings").Length == 0;
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:batterysaver") { UseShellExecute = true });
            AutomationElement? window = WaitForWindow(TimeSpan.FromSeconds(8));
            if (window is null)
            {
                return null;
            }

            AutomationElement? toggle = WaitForToggle(window, TimeSpan.FromSeconds(6));
            if (toggle is null
                || !toggle.TryGetCurrentPattern(TogglePattern.Pattern, out object? raw)
                || raw is not TogglePattern pattern)
            {
                return null;
            }

            if ((pattern.Current.ToggleState == ToggleState.On) != on)
            {
                pattern.Toggle();
            }

            return WaitUntil(pattern, on, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: energy saver toggle failed — {ex.Message}");
            return null;
        }
        finally
        {
            if (launched)
            {
                CloseSettings();
            }
        }
    }

    private static bool? WaitUntil(TogglePattern pattern, bool on, TimeSpan budget)
    {
        long deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;
        while (true)
        {
            try
            {
                if ((pattern.Current.ToggleState == ToggleState.On) == on)
                {
                    return on;
                }
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return null;
            }

            Thread.Sleep(100);
        }
    }

    private static AutomationElement? WaitForWindow(TimeSpan budget)
    {
        long deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            AutomationElement? window = FindSettingsWindow();
            if (window is not null)
            {
                Park(window);
                return window;
            }

            Thread.Sleep(200);
        }

        return null;
    }

    private static AutomationElement? FindSettingsWindow()
    {
        foreach (string name in new[] { "Ayarlar", "Settings" })
        {
            try
            {
                AutomationElement? window = AutomationElement.RootElement.FindFirst(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.NameProperty, name));
                if (window is not null)
                {
                    return window;
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
            {
                Debug.WriteLine($"WharfBar: settings window lookup failed — {ex.Message}");
            }
        }

        return null;
    }

    private static AutomationElement? WaitForToggle(AutomationElement window, TimeSpan budget)
    {
        long deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;
        var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, ToggleId);
        while (Environment.TickCount64 < deadline)
        {
            Park(window);
            try
            {
                Expand(window);
                AutomationElement? found = window.FindFirst(TreeScope.Descendants, condition);
                if (found is not null)
                {
                    return found;
                }
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }

            Thread.Sleep(200);
        }

        return null;
    }

    private static void Expand(AutomationElement window)
    {
        AutomationElementCollection buttons;
        try
        {
            buttons = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, ExpandId));
        }
        catch (ElementNotAvailableException)
        {
            return;
        }

        TreeWalker walker = TreeWalker.ControlViewWalker;
        foreach (AutomationElement button in buttons)
        {
            AutomationElement? parent = walker.GetParent(button);
            if (parent is null || !IsEnergySaverGroup(parent.Current.Name))
            {
                continue;
            }

            if (button.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? raw)
                && raw is ExpandCollapsePattern expand
                && expand.Current.ExpandCollapseState != ExpandCollapseState.Expanded)
            {
                expand.Expand();
            }

            return;
        }
    }

    private static bool IsEnergySaverGroup(string name)
        => name.StartsWith("Enerji tasarrufu", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Energy saver", StringComparison.OrdinalIgnoreCase);

    private static void Park(AutomationElement window)
    {
        try
        {
            int hwnd = window.Current.NativeWindowHandle;
            if (hwnd == 0)
            {
                return;
            }

            NativeMethods.SetWindowPos(
                (IntPtr)hwnd,
                IntPtr.Zero,
                -32000,
                -32000,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
        catch (ElementNotAvailableException)
        {
            // Pencere kapanmışsa park etmeye gerek yok.
        }
    }

    private static void CloseSettings()
    {
        foreach (Process process in Process.GetProcessesByName("SystemSettings"))
        {
            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(800))
                {
                    process.Kill(entireProcessTree: false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar: settings close failed — {ex.Message}");
            }
        }
    }
}
