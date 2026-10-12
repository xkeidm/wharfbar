using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Attention and minimize signals. Flash uses <c>RegisterShellHookWindow</c> on a window
/// this process already owns. Minimize uses an out-of-context WinEvent so nothing is
/// injected into the other process.
/// </summary>
internal static class DockShellHook
{
    internal const int HSHELL_WINDOWACTIVATED = 4;
    internal const int HSHELL_REDRAW = 6;
    internal const int HSHELL_HIGHBIT = 0x8000;
    internal const int HSHELL_FLASH = HSHELL_REDRAW | HSHELL_HIGHBIT;
    internal const int HSHELL_RUDEAPPACTIVATED = HSHELL_WINDOWACTIVATED | HSHELL_HIGHBIT;

    internal const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    internal const int OBJID_WINDOW = 0;

    internal delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    internal static uint RegisterMessage() => RegisterWindowMessageW("SHELLHOOK");

    internal static bool TryRegister(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!RegisterShellHookWindow(hwnd))
        {
            Debug.WriteLine($"WharfBar RegisterShellHookWindow failed: {Marshal.GetLastWin32Error()}");
            return false;
        }

        return true;
    }

    internal static void Unregister(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!DeregisterShellHookWindow(hwnd))
        {
            Debug.WriteLine($"WharfBar DeregisterShellHookWindow failed: {Marshal.GetLastWin32Error()}");
        }
    }

    internal static IntPtr TryHookMinimize(WinEventProc proc)
    {
        ArgumentNullException.ThrowIfNull(proc);
        IntPtr hook = SetWinEventHook(
            EVENT_SYSTEM_MINIMIZESTART,
            EVENT_SYSTEM_MINIMIZESTART,
            IntPtr.Zero,
            proc,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hook == IntPtr.Zero)
        {
            Debug.WriteLine($"WharfBar SetWinEventHook(MINIMIZESTART) failed: {Marshal.GetLastWin32Error()}");
        }

        return hook;
    }

    internal static void Unhook(IntPtr hook)
    {
        if (hook == IntPtr.Zero)
        {
            return;
        }

        if (!UnhookWinEvent(hook))
        {
            Debug.WriteLine($"WharfBar UnhookWinEvent failed: {Marshal.GetLastWin32Error()}");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterShellHookWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeregisterShellHookWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
