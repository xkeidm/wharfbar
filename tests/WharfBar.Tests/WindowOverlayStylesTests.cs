using System.Runtime.InteropServices;
using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class WindowOverlayStylesTests
{
    [Fact]
    public void ExcludeFromTaskSwitcher_SetsToolWindowStyle()
    {
        IntPtr hwnd = CreateDummyWindow();
        Assert.NotEqual(IntPtr.Zero, hwnd);

        try
        {
            int before = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            Assert.Equal(0, before & NativeMethods.WS_EX_TOOLWINDOW);

            WindowOverlayStyles.ExcludeFromTaskSwitcher(hwnd);

            int after = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            Assert.NotEqual(0, after & NativeMethods.WS_EX_TOOLWINDOW);
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    [Fact]
    public void ExcludeFromTaskSwitcher_IsIdempotent()
    {
        IntPtr hwnd = CreateDummyWindow();
        Assert.NotEqual(IntPtr.Zero, hwnd);

        try
        {
            WindowOverlayStyles.ExcludeFromTaskSwitcher(hwnd);
            WindowOverlayStyles.ExcludeFromTaskSwitcher(hwnd);

            int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            Assert.NotEqual(0, ex & NativeMethods.WS_EX_TOOLWINDOW);
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    private static readonly WndProcDelegate WndProc = new(DefWindowProc);

    private static IntPtr CreateDummyWindow()
    {
        const string className = "WharfBarTestsOverlayDummy";

        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProc),
            lpszClassName = className,
        };
        RegisterClassEx(ref wndClass);

        return CreateWindowEx(
            0,
            className,
            "WharfBar overlay dummy",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
