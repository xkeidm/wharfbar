namespace WharfBar.Native;

/// <summary>
/// Notification codes delivered through the AppBar callback message.
/// </summary>
internal enum AppBarNotification
{
    StateChange = NativeMethods.ABN_STATECHANGE,
    PosChanged = NativeMethods.ABN_POSCHANGED,
    FullScreenApp = NativeMethods.ABN_FULLSCREENAPP,
    WindowArrange = NativeMethods.ABN_WINDOWARRANGE,
}

/// <summary>
/// Wrapper around <c>SHAppBarMessage</c> for a single window.
///
/// Lifecycle: <see cref="Register"/> (ABM_NEW) → <see cref="Reserve"/> (ABM_QUERYPOS + ABM_SETPOS)
/// → <see cref="Remove"/> (ABM_REMOVE). Skipping ABM_REMOVE leaves the reserved work area
/// stranded until explorer.exe restarts, so <see cref="Remove"/> is idempotent and safe to call
/// from every shutdown path.
/// </summary>
internal sealed class AppBar : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly uint _edge;
    private int _removed;
    private bool _isRegistered;

    internal AppBar(IntPtr hwnd, uint callbackMessage, uint edge = NativeMethods.ABE_TOP)
    {
        _hwnd = hwnd;
        _edge = edge;
        CallbackMessage = callbackMessage;
    }

    /// <summary>Registered window message the shell uses to deliver <c>ABN_*</c> notifications.</summary>
    internal uint CallbackMessage { get; }

    internal bool IsRegistered => _isRegistered;

    /// <summary>True once <see cref="Remove"/> has run; further registration is refused.</summary>
    internal bool IsRemoved => Volatile.Read(ref _removed) != 0;

    internal bool Register()
    {
        if (IsRemoved || _isRegistered)
        {
            return _isRegistered;
        }

        APPBARDATA data = CreateData();
        data.uCallbackMessage = CallbackMessage;
        _isRegistered = NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data) != 0;
        return _isRegistered;
    }

    /// <summary>
    /// Re-runs ABM_NEW after the shell dropped our registration (explorer.exe restart).
    /// The old registration died with the previous shell instance, so no ABM_REMOVE first.
    /// </summary>
    internal bool ReRegister()
    {
        if (IsRemoved)
        {
            return false;
        }

        _isRegistered = false;
        return Register();
    }

    /// <summary>
    /// Asks the shell to approve <paramref name="desired"/> and commits the result.
    /// Returns the rect the window should actually occupy, in physical pixels.
    /// </summary>
    internal RECT Reserve(RECT desired)
    {
        if (!_isRegistered || IsRemoved)
        {
            return desired;
        }

        int height = desired.Height;
        int width = desired.Width;

        APPBARDATA data = CreateData();
        data.rc = desired;
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref data);

        // QUERYPOS only moves the edge it owns; the opposite edge must be restored by us
        // or the bar collapses to zero thickness.
        switch (_edge)
        {
            case NativeMethods.ABE_TOP:
                data.rc.Bottom = data.rc.Top + height;
                break;
            case NativeMethods.ABE_BOTTOM:
                data.rc.Top = data.rc.Bottom - height;
                break;
            case NativeMethods.ABE_LEFT:
                data.rc.Right = data.rc.Left + width;
                break;
            case NativeMethods.ABE_RIGHT:
                data.rc.Left = data.rc.Right - width;
                break;
        }

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);
        return data.rc;
    }

    internal void NotifyActivated()
    {
        if (!_isRegistered || IsRemoved)
        {
            return;
        }

        APPBARDATA data = CreateData();
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_ACTIVATE, ref data);
    }

    internal void NotifyWindowPosChanged()
    {
        if (!_isRegistered || IsRemoved)
        {
            return;
        }

        APPBARDATA data = CreateData();
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_WINDOWPOSCHANGED, ref data);
    }

    /// <summary>
    /// Releases the reserved work area. Idempotent and thread-safe: only the first caller
    /// reaches the shell, so wiring this to several shutdown events is intentional.
    /// </summary>
    internal void Remove()
    {
        if (Interlocked.Exchange(ref _removed, 1) != 0)
        {
            return;
        }

        APPBARDATA data = CreateData();
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
        _isRegistered = false;
    }

    public void Dispose() => Remove();

    /// <summary>Decodes an AppBar callback message; returns false when it is not ours.</summary>
    internal bool TryGetNotification(int message, IntPtr wParam, out AppBarNotification notification)
    {
        if ((uint)message != CallbackMessage || CallbackMessage == 0)
        {
            notification = default;
            return false;
        }

        notification = (AppBarNotification)wParam.ToInt32();
        return true;
    }

    private APPBARDATA CreateData() => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
        uEdge = _edge,
    };
}
