namespace WharfBar.Native;

/// <summary>The top-level window that currently parents <c>SHELLDLL_DefView</c>.</summary>
internal static class DesktopIconHost
{
    internal static IntPtr TryGet()
    {
        IntPtr host = IntPtr.Zero;
        NativeMethods.EnumWindows((top, _) =>
        {
            IntPtr defView = NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                return true;
            }

            host = top;
            return false;
        }, IntPtr.Zero);

        return host;
    }
}
