using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Real mouse clicks at absolute screen coordinates (UIA tray fallback: right-click / double-click
/// cannot be expressed through UIA, so the caller peeks the real taskbar and replays a physical
/// click on the button's on-screen position).
/// </summary>
internal static class SendInputMouse
{
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Moves the cursor to <paramref name="x"/>/<paramref name="y"/> (screen coords), replays the
    /// click, then restores the previous cursor position.
    /// </summary>
    internal static void Click(int x, int y, TrayClick kind)
    {
        if (!GetCursorPos(out POINT previous))
        {
            previous = default;
        }

        _ = SetCursorPos(x, y);
        try
        {
            switch (kind)
            {
                case TrayClick.Right:
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    break;
                case TrayClick.DoubleLeft:
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;
                default:
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;
            }
        }
        finally
        {
            _ = SetCursorPos(previous.X, previous.Y);
        }
    }
}
