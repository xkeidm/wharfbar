namespace WharfBar.Native;

/// <summary>
/// Temporarily hides a foreign top-level window from composition so a dock cue can own the pixels.
/// Prefer DWM cloak; fall back to WS_EX_LAYERED + alpha 0. Dispose always restores.
/// </summary>
internal sealed class DockWindowCloak : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly bool _usedCloak;
    private readonly bool _addedLayered;
    private readonly int _priorExStyle;
    private bool _disposed;

    private DockWindowCloak(IntPtr hwnd, bool usedCloak, bool addedLayered, int priorExStyle)
    {
        _hwnd = hwnd;
        _usedCloak = usedCloak;
        _addedLayered = addedLayered;
        _priorExStyle = priorExStyle;
    }

    /// <summary>Returns a live hide token, or null if the window could not be hidden.</summary>
    internal static DockWindowCloak? TryHide(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        if (TrySetCloaked(hwnd, cloaked: true))
        {
            return new DockWindowCloak(hwnd, usedCloak: true, addedLayered: false, priorExStyle: 0);
        }

        int prior = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        bool alreadyLayered = (prior & NativeMethods.WS_EX_LAYERED) != 0;
        if (!alreadyLayered)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, prior | NativeMethods.WS_EX_LAYERED);
        }

        if (!NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA))
        {
            if (!alreadyLayered)
            {
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, prior);
            }

            return null;
        }

        return new DockWindowCloak(hwnd, usedCloak: false, addedLayered: !alreadyLayered, priorExStyle: prior);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hwnd))
        {
            return;
        }

        if (_usedCloak)
        {
            _ = TrySetCloaked(_hwnd, cloaked: false);
            return;
        }

        _ = NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 255, NativeMethods.LWA_ALPHA);
        if (_addedLayered)
        {
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, _priorExStyle);
        }
    }

    private static bool TrySetCloaked(IntPtr hwnd, bool cloaked)
    {
        int value = cloaked ? 1 : 0;
        return NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAK, ref value, sizeof(int))
            == NativeMethods.S_OK;
    }
}
