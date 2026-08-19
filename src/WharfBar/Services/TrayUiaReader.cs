using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>One third-party tray app button exposed by UIA (Windows 11 25H2+ fallback).</summary>
internal sealed record UiaTrayButton(string Name, AutomationElement Element);

/// <summary>
/// Reads the tray app icons via UI Automation — the only supported surface on Windows 11 25H2,
/// where the classic comctl32 <c>ToolbarWindow32</c> tray was removed (TB_* messages no longer
/// work). Requires the taskbar to be shown (caller stealth-parks it); yields button names and
/// UIA elements but no icon bitmaps — the mirror shows a letter glyph instead.
/// </summary>
internal static class TrayUiaReader
{
    private const string AppButtonClass = "SystemTray.NormalButton";
    private const string TrayClass = "Shell_TrayWnd";

    /// <summary>
    /// Pure classifier: only <c>SystemTray.NormalButton</c> elements are tray app icons; system
    /// buttons use other classes (AccentButton, OmniButton*, ShowDesktopButton, task list peers),
    /// and the input indicator uses NormalButton but is filtered by its tooltip.
    /// </summary>
    internal static bool IsAppIcon(string className, string? name)
    {
        if (!string.Equals(className, AppButtonClass, StringComparison.Ordinal))
        {
            return false;
        }

        return !TraySystemIconFilter.IsSystemIcon(name)
            && !TraySystemIconFilter.IsWharfBarIcon(name);
    }

    /// <summary>
    /// Enumerates tray app buttons under <c>Shell_TrayWnd</c>. Returns false when the taskbar is
    /// not present or its UIA tree is not exposed (e.g. taskbar fully hidden).
    /// </summary>
    internal static bool TryGetTrayButtons(out IReadOnlyList<UiaTrayButton> buttons)
    {
        buttons = [];

        IntPtr tray = NativeMethods.FindWindow(TrayClass, null);
        if (tray == IntPtr.Zero)
        {
            return false;
        }

        AutomationElement? root;
        try
        {
            root = AutomationElement.FromHandle(tray);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: UIA tray root failed — {ex.Message}");
            return false;
        }

        if (root is null)
        {
            return false;
        }

        AutomationElementCollection found;
        try
        {
            found = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, AppButtonClass));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: UIA tray scan failed — {ex.Message}");
            return false;
        }

        var result = new List<UiaTrayButton>();
        foreach (AutomationElement el in found)
        {
            string name;
            try
            {
                name = el.Current.Name ?? string.Empty;
            }
            catch (Exception)
            {
                continue;
            }

            if (IsAppIcon(AppButtonClass, name))
            {
                result.Add(new UiaTrayButton(name, el));
            }
        }

        buttons = result;
        return true;
    }

    /// <summary>Left-click via UIA InvokePattern (works on the UI thread, STA).</summary>
    internal static bool TryInvoke(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern)
                && pattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }
        }
        catch (ElementNotAvailableException ex)
        {
            Debug.WriteLine($"WharfBar: UIA invoke element lost — {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"WharfBar: UIA invoke failed — {ex.Message}");
        }

        return false;
    }

    /// <summary>On-screen bounding rectangle of the button (empty when the element is gone).</summary>
    internal static Rect GetScreenRect(AutomationElement el)
    {
        try
        {
            return el.Current.BoundingRectangle;
        }
        catch (Exception)
        {
            return Rect.Empty;
        }
    }
}
