using System.Runtime.InteropServices;
using System.Threading;

namespace WharfBar.Native;

/// <summary>
/// Real mouse clicks at absolute screen coordinates (UIA tray fallback: right-click / double-click
/// cannot be expressed through UIA, so the caller peeks the real taskbar and replays a physical
/// click on the button's on-screen position).
/// </summary>
internal static class SendInputMouse
{
    private const uint InputMouse = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    /// <summary>
    /// How long the cursor stays on the real tray button after a right-click. The owner reads
    /// <c>GetCursorPos</c> inside <c>WM_RBUTTONUP</c> / <c>WM_CONTEXTMENU</c>; restoring sooner
    /// reports the top-bar position and the menu is ignored or never shown.
    /// </summary>
    internal const int RightClickCursorHoldMs = 160;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Maps a physical screen pixel into the 0–65535 range <c>SendInput</c> expects when
    /// <c>MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK</c> is set.
    /// </summary>
    internal static int NormalizeAbsolute(int pixel, int origin, int span)
    {
        if (span <= 1)
        {
            return 0;
        }

        double normalized = (pixel - origin) * 65535.0 / (span - 1);
        if (normalized < 0)
        {
            return 0;
        }

        if (normalized > 65535)
        {
            return 65535;
        }

        return (int)Math.Round(normalized);
    }

    /// <summary>Moves the cursor with a real (absolute) move packet so hover tracking sees it.</summary>
    internal static void MoveTo(int x, int y)
    {
        _ = SetCursorPos(x, y);
        int ax = NormalizeAbsolute(x, GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int ay = NormalizeAbsolute(y, GetSystemMetrics(SM_YVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
        INPUT[] move = [Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, ax, ay)];
        _ = NativeMethods.SendInput(1, move, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Remembers the cursor now; disposing the lease puts it back (idempotent).</summary>
    internal static IDisposable HoldCursorHome()
    {
        if (!GetCursorPos(out POINT home))
        {
            home = default;
        }

        return new CursorLease(home.X, home.Y);
    }

    /// <summary>
    /// Right-clicks at physical pixel (<paramref name="x"/>, <paramref name="y"/>) and leaves
    /// the cursor there until the returned lease is disposed. Tray owners read
    /// <c>GetCursorPos</c> while building the menu; restoring before that makes the click
    /// look like it landed on the top bar and the menu never opens.
    /// </summary>
    internal static IDisposable RightClickHoldingCursor(int x, int y)
    {
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        if (!GetCursorPos(out POINT previous))
        {
            previous = default;
        }

        _ = SetCursorPos(x, y);
        SendAbsoluteClick(x, y);
        return new CursorLease(previous.X, previous.Y);
    }

    /// <summary>
    /// Replays <paramref name="kind"/> at physical screen pixel (<paramref name="x"/>,
    /// <paramref name="y"/>), then restores the cursor. A right-click is an absolute
    /// <c>SendInput</c> packet and the cursor stays on that pixel until the owner has read
    /// <c>GetCursorPos</c>. Left clicks stay on <c>mouse_event</c> at the moved cursor.
    /// </summary>
    internal static void Click(int x, int y, TrayClick kind)
    {
        // TrackPopupMenu in the icon owner fails unless the process that just received the
        // user's click grants foreground. Call it immediately before the synthetic input.
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        if (!GetCursorPos(out POINT previous))
        {
            previous = default;
        }

        _ = SetCursorPos(x, y);
        try
        {
            if (kind == TrayClick.Right)
            {
                // Absolute packet: mouse_event records no coordinates, and restoring the
                // cursor before the owner runs retargets the click onto the top bar.
                SendAbsoluteClick(x, y);
                Thread.Sleep(RightClickCursorHoldMs);
            }
            else if (kind == TrayClick.DoubleLeft)
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
            else
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
        }
        finally
        {
            _ = SetCursorPos(previous.X, previous.Y);
        }
    }

    private static void SendAbsoluteClick(int x, int y)
    {
        int ax = NormalizeAbsolute(x, GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int ay = NormalizeAbsolute(y, GetSystemMetrics(SM_YVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
        INPUT[] inputs = BuildClick(ax, ay, right: true, clicks: 1);
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
        {
            TrayPeekDiagnostics.LogNote(
                $"click sendinput sent={sent}/{inputs.Length} err={Marshal.GetLastWin32Error()}");
        }
    }

    private static INPUT[] BuildClick(int absoluteX, int absoluteY, bool right, int clicks)
    {
        uint down = (right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN)
            | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        uint up = (right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP)
            | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        uint move = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        var inputs = new INPUT[1 + (clicks * 2)];
        inputs[0] = Mouse(move, absoluteX, absoluteY);
        int n = 1;
        for (int i = 0; i < clicks; i++)
        {
            inputs[n++] = Mouse(down, absoluteX, absoluteY);
            inputs[n++] = Mouse(up, absoluteX, absoluteY);
        }

        return inputs;
    }

    private sealed class CursorLease : IDisposable
    {
        private readonly int _x;
        private readonly int _y;
        private bool _disposed;

        internal CursorLease(int x, int y)
        {
            _x = x;
            _y = y;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _ = SetCursorPos(_x, _y);
        }
    }

    private static INPUT Mouse(uint flags, int absoluteX, int absoluteY) => new()
    {
        type = InputMouse,
        U = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dx = absoluteX,
                dy = absoluteY,
                mouseData = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };
}
