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

    /// <summary>
    /// Left-click via UIA <c>InvokePattern</c> (the standard action; works on the UI thread, STA).
    /// <paramref name="stale"/> true when the element no longer exists — the caller then re-reads
    /// the live tray and replays the gesture on a fresh element; a missing pattern is not stale.
    /// </summary>
    internal static bool TryInvoke(AutomationElement el, out bool stale)
    {
        stale = false;
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
            stale = true;
            Debug.WriteLine($"WharfBar: UIA invoke element lost — {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"WharfBar: UIA invoke failed — {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Gives the tray button keyboard focus (UI Automation <c>SetFocus</c>). That is what lets
    /// the Menu key open the owner's context menu without a physical right click.
    /// </summary>
    internal static bool TryFocus(AutomationElement el, out bool stale)
    {
        stale = false;
        try
        {
            el.SetFocus();
            return true;
        }
        catch (ElementNotAvailableException ex)
        {
            stale = true;
            Debug.WriteLine($"WharfBar: UIA focus element lost — {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"WharfBar: UIA focus failed — {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// True when the element currently owns keyboard focus. The menu key goes to whatever has
    /// focus, so it must never be sent unless this check proves the target button is focused.
    /// </summary>
    internal static bool IsFocused(AutomationElement el)
    {
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return false;
            }

            int[]? target = el.GetRuntimeId();
            int[]? current = focused.GetRuntimeId();
            return target is not null && current is not null && target.SequenceEqual(current);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the tray button's own context menu the way a keyboard user does: focus the button,
    /// then press the Menu key (VK_APPS). Focus is verified first — sending the key while another
    /// window is focused would open a menu in the user's application instead.
    /// <paramref name="beforeMenuKey"/> runs after the focus change and immediately before the
    /// key: the caller arms the pointer clip there, because any foreground change releases an
    /// earlier clip and the owner warps the pointer right after the key.
    /// </summary>
    internal static bool TryOpenContextMenu(AutomationElement el, out bool stale, Action? beforeMenuKey = null)
    {
        stale = false;
        if (!TryFocus(el, out stale))
        {
            return false;
        }

        if (!IsFocused(el))
        {
            Debug.WriteLine("WharfBar: tray button did not take keyboard focus — menu key not sent");
            return false;
        }

        beforeMenuKey?.Invoke();
        return SendInputKeys.TrySendMenuKey();
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
