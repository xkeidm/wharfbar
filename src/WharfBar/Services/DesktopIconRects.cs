using System.Diagnostics;
using System.Windows.Automation;
using WharfBar.Native;

namespace WharfBar.Services;

internal static class DesktopIconRects
{
    internal static IReadOnlyList<DipRect> KeepUsable(IReadOnlyList<DipRect> raw)
    {
        var kept = new List<DipRect>();
        foreach (DipRect rect in raw)
        {
            if (rect.Width > 0 && rect.Height > 0
                && double.IsFinite(rect.X) && double.IsFinite(rect.Y)
                && double.IsFinite(rect.Width) && double.IsFinite(rect.Height))
            {
                kept.Add(rect);
            }
        }

        return kept;
    }

    /// <summary>
    /// Reads icon bounds from the desktop list. Call this on an STA thread.
    /// An empty list means no icons. A failure is logged and also returns empty.
    /// </summary>
    internal static IReadOnlyList<DipRect> ReadInDip()
    {
        try
        {
            IntPtr host = DesktopIconHost.TryGet();
            if (host == IntPtr.Zero)
            {
                return [];
            }

            IntPtr defView = NativeMethods.FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr list = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            if (list == IntPtr.Zero)
            {
                return [];
            }

            AutomationElement root = AutomationElement.FromHandle(list);
            AutomationElementCollection items = root.FindAll(TreeScope.Children, Condition.TrueCondition);
            var raw = new List<DipRect>(items.Count);
            foreach (AutomationElement item in items)
            {
                System.Windows.Rect bounds = item.Current.BoundingRectangle;
                if (bounds.IsEmpty)
                {
                    continue;
                }

                uint dpi = NativeMethods.GetDpiForWindow(list);
                if (dpi == 0)
                {
                    dpi = 96;
                }

                raw.Add(new DipRect(
                    WidgetScale.ToDip((int)Math.Round(bounds.X), dpi),
                    WidgetScale.ToDip((int)Math.Round(bounds.Y), dpi),
                    WidgetScale.ToDip((int)Math.Round(bounds.Width), dpi),
                    WidgetScale.ToDip((int)Math.Round(bounds.Height), dpi)));
            }

            return KeepUsable(raw);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icon read failed: {ex.Message}");
            return [];
        }
    }
}
