using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Out-of-context WinEvent hooks for the top-bar strip guard: the end of every interactive
/// move/resize and every newly shown window. Out-of-context means nothing is injected into the
/// other process (the same pattern as <see cref="DockShellHook"/>); the callback runs on the
/// installing thread's message loop, so it must stay cheap and only post work.
/// </summary>
internal static class WindowEventHook
{
    /// <summary>An interactive move or resize has finished.</summary>
    internal const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;

    /// <summary>A window has been shown (also fired when a window is restored from minimized).</summary>
    internal const uint EVENT_OBJECT_SHOW = 0x8002;

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

    internal static IntPtr TryHookMoveSizeEnd(WinEventProc proc)
        => TryHook(EVENT_SYSTEM_MOVESIZEEND, "MOVESIZEEND", proc);

    internal static IntPtr TryHookShow(WinEventProc proc)
        => TryHook(EVENT_OBJECT_SHOW, "OBJECT_SHOW", proc);

    private static IntPtr TryHook(uint eventType, string name, WinEventProc proc)
    {
        ArgumentNullException.ThrowIfNull(proc);
        IntPtr hook = SetWinEventHook(
            eventType,
            eventType,
            IntPtr.Zero,
            proc,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hook == IntPtr.Zero)
        {
            Debug.WriteLine($"WharfBar SetWinEventHook({name}) failed: {Marshal.GetLastWin32Error()}");
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
