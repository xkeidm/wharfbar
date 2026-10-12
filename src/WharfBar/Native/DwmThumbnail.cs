using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>DWM thumbnail of one live window, drawn into a flyout HWND the dock owns.</summary>
internal static class DwmThumbnail
{
    internal const uint DWM_TNP_RECTDESTINATION = 0x00000001;
    internal const uint DWM_TNP_OPACITY = 0x00000004;
    internal const uint DWM_TNP_VISIBLE = 0x00000008;
    internal const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

    internal static bool IsCloaked(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return true;
        }

        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_CLOAKED,
            out int cloaked,
            sizeof(int));
        return hr == NativeMethods.S_OK && cloaked != 0;
    }

    internal static bool TryRegister(IntPtr destination, IntPtr source, out IntPtr thumbnail)
    {
        thumbnail = IntPtr.Zero;
        if (destination == IntPtr.Zero || source == IntPtr.Zero)
        {
            return false;
        }

        int hr = DwmRegisterThumbnail(destination, source, out thumbnail);
        if (hr != NativeMethods.S_OK || thumbnail == IntPtr.Zero)
        {
            Debug.WriteLine($"WharfBar DwmRegisterThumbnail failed: 0x{hr:X8}");
            thumbnail = IntPtr.Zero;
            return false;
        }

        return true;
    }

    internal static void Update(IntPtr thumbnail, int width, int height)
    {
        if (thumbnail == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY,
            rcDestination = new RECT { Left = 0, Top = 0, Right = width, Bottom = height },
            opacity = 255,
            fVisible = true,
            fSourceClientAreaOnly = false,
        };

        int hr = DwmUpdateThumbnailProperties(thumbnail, ref props);
        if (hr != NativeMethods.S_OK)
        {
            Debug.WriteLine($"WharfBar DwmUpdateThumbnailProperties failed: 0x{hr:X8}");
        }
    }

    internal static void Unregister(IntPtr thumbnail)
    {
        if (thumbnail == IntPtr.Zero)
        {
            return;
        }

        int hr = DwmUnregisterThumbnail(thumbnail);
        if (hr != NativeMethods.S_OK)
        {
            Debug.WriteLine($"WharfBar DwmUnregisterThumbnail failed: 0x{hr:X8}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        internal uint dwFlags;
        internal RECT rcDestination;
        internal RECT rcSource;
        internal byte opacity;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool fVisible;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr thumb);
}
