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
    internal static IntPtr FindTrayHwnd() => NativeMethods.FindWindow(TrayClass, null);

    /// <summary>
    /// Enumerates tray app buttons. Roots at the DESKTOP element, not at
    /// <c>Shell_TrayWnd</c>: with the auto-hide state on (strip release, ADR-005 note 5)
    /// explorer parks and clamps the tray window at a ~2px edge sliver and that window's
    /// UIA subtree virtualizes away — but the desktop-root walk still reaches the live
    /// <c>SystemTray.NormalButton</c> elements, so reads need no peek/show at all.
    /// MUST run on an STA thread (desktop-scope UIA walks return empty on MTA).
    /// Returns null when the walk fails; an empty list means no icons exposed.
    /// </summary>
    internal static IReadOnlyList<UiaTrayButton>? TryGetTrayButtons()
    {
        AutomationElementCollection found;
        try
        {
            found = AutomationElement.RootElement.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, AppButtonClass));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: UIA tray scan failed — {ex.Message}");
            return null;
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

        return result;
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

    /// <summary>
    /// Tight bounds of the icon art: the button's child <c>Image</c> element rect (avoids the
    /// button's hover padding). Falls back to the button rect when no child image is exposed.
    /// </summary>
    internal static Rect GetIconRect(AutomationElement button)
    {
        try
        {
            AutomationElement? image = button.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image));
            if (image is not null)
            {
                Rect r = image.Current.BoundingRectangle;
                if (!r.IsEmpty && r.Width > 0 && r.Height > 0)
                {
                    return r;
                }
            }
        }
        catch (Exception)
        {
        }

        return GetScreenRect(button);
    }
}
