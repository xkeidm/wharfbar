using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// One process-wide <c>WH_KEYBOARD_LL</c> hook. It runs on a dedicated message-loop thread so a busy
/// WPF dispatcher can never make Windows skip it (<c>LowLevelHooksTimeout</c>); the handler must still
/// answer immediately, because the hook holds the desktop input queue while it runs.
/// </summary>
internal sealed class LowLevelKeyboardHook : IDisposable
{
    /// <summary>A keyboard event. Returning true swallows it, false passes it on.</summary>
    internal delegate bool Handler(uint virtualKey, bool keyUp, bool injected);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;
    private const uint PM_NOREMOVE = 0x0000;
    private const uint LLKHF_INJECTED = 0x10;

    private readonly Handler _handler;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _stopped = new(false);
    private KeyboardProc? _proc;
    private Thread? _thread;
    private IntPtr _hook;
    private uint _threadId;
    private int _installError;
    private bool _installed;
    private bool _disposed;

    internal LowLevelKeyboardHook(Handler handler) =>
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>Installs the hook on its own thread. False when Windows refuses it.</summary>
    internal bool Install(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
        {
            return _installed;
        }

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "WharfBar keyboard hook",
        };
        _thread.Start();
        if (!_ready.Wait(timeout))
        {
            Debug.WriteLine("WharfBar: keyboard hook did not install in time");
            return false;
        }

        if (!_installed)
        {
            Debug.WriteLine($"WharfBar: keyboard hook install failed (error 0x{_installError:X})");
        }

        return _installed;
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        // Force the message queue into existence so Dispose's WM_QUIT cannot be lost.
        _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);

        _proc = Callback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            _installError = Marshal.GetLastWin32Error();
            // A null module handle is the documented form when the hook procedure lives in the
            // current process; some hosts reject the main-module handle for low-level hooks.
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
            if (_hook == IntPtr.Zero)
            {
                _installError = Marshal.GetLastWin32Error();
            }
        }

        _installed = _hook != IntPtr.Zero;
        _ready.Set();
        if (!_installed)
        {
            _stopped.Set();
            return;
        }

        if (_disposed)
        {
            // Raced Install's timeout: the caller gave up before the hook was ready.
            _ = UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _stopped.Set();
            return;
        }

        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
            // No windows on this thread; the hook callback is invoked directly by the system.
        }

        _ = UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _stopped.Set();
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                int message = wParam.ToInt32();
                if (message is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    bool keyUp = message is WM_KEYUP or WM_SYSKEYUP;
                    bool injected = (data.flags & LLKHF_INJECTED) != 0;
                    if (_handler(data.vkCode, keyUp, injected))
                    {
                        return (IntPtr)1;
                    }
                }
            }
            catch (Exception ex)
            {
                // An exception must never escape a native callback: it would take the process down.
                Debug.WriteLine($"[WharfBar] keyboard hook handler failed: {ex.Message}");
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_thread is not null && _threadId != 0)
        {
            // Same thread id: PostThreadMessage works without a window handle.
            _ = PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _ = _stopped.Wait(TimeSpan.FromSeconds(2));
            _thread = null;
            _threadId = 0;
        }

        // The gates stay undisposed on purpose: a hook thread that finishes late could still set
        // them, and this is one process-lifetime object (App creates it once).
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }

    private delegate IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, KeyboardProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG message, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG message, IntPtr hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
