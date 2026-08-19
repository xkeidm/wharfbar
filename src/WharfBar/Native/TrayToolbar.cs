using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>How a mirrored tray icon click is replayed onto the real toolbar.</summary>
internal enum TrayClick
{
    Left,
    Right,
    DoubleLeft,
}

/// <summary>Metadata for one button in the tray toolbar.</summary>
internal sealed record TrayButtonInfo(int CommandId, int ImageIndex, string Tooltip);

/// <summary>
/// Read-only access to the real Windows tray toolbar (ADR-009). Uses documented comctl32
/// toolbar messages (TB_*) and forwards clicks by sending mouse messages at the button rect —
/// never ReadProcessMemory or undocumented APIs.
/// </summary>
internal static class TrayToolbar
{
    private const uint TimeoutMs = 500;
    private const int MaxTooltipChars = 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct TBBUTTONINFO
    {
        public uint cbSize;
        public uint dwMask;
        public int idCommand;
        public int iImage;
        public byte fsState;
        public byte fsStyle;
        public ushort cx;
        public IntPtr lParam;
        public IntPtr pszText;
        public int cchText;
    }

    internal static IntPtr FindToolbarHwnd()
    {
        IntPtr tray = TaskbarNative.FindTrayHwnd();
        if (tray == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr notify = NativeMethods.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr pager = NativeMethods.FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
        IntPtr parent = pager != IntPtr.Zero ? pager : notify;

        IntPtr toolbar = IntPtr.Zero;
        while (true)
        {
            toolbar = NativeMethods.FindWindowEx(parent, toolbar, "ToolbarWindow32", null);
            if (toolbar == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (SendTimeout(toolbar, NativeMethods.TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64() > 0)
            {
                return toolbar;
            }
        }
    }

    internal static IReadOnlyList<TrayButtonInfo> ReadButtons(IntPtr toolbar)
    {
        long count = SendTimeout(toolbar, NativeMethods.TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        if (count <= 0 || count > 256)
        {
            return [];
        }

        var result = new List<TrayButtonInfo>((int)count);
        for (int i = 0; i < count; i++)
        {
            if (TryReadButton(toolbar, i, out TrayButtonInfo info))
            {
                result.Add(info);
            }
        }

        return result;
    }

    internal static IntPtr GetImageList(IntPtr toolbar) =>
        SendTimeout(toolbar, NativeMethods.TB_GETIMAGELIST, IntPtr.Zero, IntPtr.Zero);

    internal static bool TryGetIcon(IntPtr toolbar, int imageIndex, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;
        if (imageIndex < 0)
        {
            return false;
        }

        IntPtr himl = GetImageList(toolbar);
        if (himl == IntPtr.Zero)
        {
            return false;
        }

        hIcon = NativeMethods.ImageList_GetIcon(himl, imageIndex, NativeMethods.ILD_TRANSPARENT);
        return hIcon != IntPtr.Zero;
    }

    internal static bool TryGetItemRect(IntPtr toolbar, int index, out RECT rect)
    {
        rect = default;
        if (index < 0)
        {
            return false;
        }

        IntPtr rectPtr = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
        try
        {
            Marshal.StructureToPtr(rect, rectPtr, false);
            IntPtr ret = SendTimeout(toolbar, NativeMethods.TB_GETITEMRECT, (IntPtr)index, rectPtr);
            if (ret.ToInt32() == 0)
            {
                return false;
            }

            rect = Marshal.PtrToStructure<RECT>(rectPtr);
            return rect.Width > 0 && rect.Height > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(rectPtr);
        }
    }

    internal static void ClickItem(IntPtr toolbar, int index, TrayClick kind)
    {
        if (!TryGetItemRect(toolbar, index, out RECT rect))
        {
            return;
        }

        int x = (rect.Left + rect.Right) / 2;
        int y = (rect.Top + rect.Bottom) / 2;
        foreach ((uint msg, IntPtr wParam, IntPtr lParam) in TrayClickPlan.Build(kind, x, y))
        {
            _ = SendTimeout(toolbar, msg, wParam, lParam);
        }
    }

    private static bool TryReadButton(IntPtr toolbar, int index, out TrayButtonInfo info)
    {
        info = new TrayButtonInfo(0, -1, string.Empty);

        IntPtr rawPtr = Marshal.AllocHGlobal(Marshal.SizeOf<TBBUTTONINFO>());
        try
        {
            var raw = new TBBUTTONINFO
            {
                cbSize = (uint)Marshal.SizeOf<TBBUTTONINFO>(),
                dwMask = (uint)(NativeMethods.TBIF_COMMAND | NativeMethods.TBIF_IMAGE),
            };
            Marshal.StructureToPtr(raw, rawPtr, false);
            IntPtr ret = SendTimeout(toolbar, NativeMethods.TB_GETBUTTONINFO, (IntPtr)index, rawPtr);
            raw = Marshal.PtrToStructure<TBBUTTONINFO>(rawPtr);
            if (ret.ToInt64() < 0)
            {
                return false;
            }

            string tooltip = ReadTooltip(toolbar, index);
            info = new TrayButtonInfo(raw.idCommand, raw.iImage, tooltip);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(rawPtr);
        }
    }

    private static string ReadTooltip(IntPtr toolbar, int index)
    {
        IntPtr textPtr = Marshal.AllocCoTaskMem(MaxTooltipChars * sizeof(char));
        try
        {
            IntPtr len = SendTimeout(toolbar, NativeMethods.TB_GETBUTTONTEXT, (IntPtr)index, textPtr);
            int n = len.ToInt32();
            if (n <= 0)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUni(textPtr, Math.Min(n, MaxTooltipChars)) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(textPtr);
        }
    }

    private static IntPtr SendTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        NativeMethods.SendMessageTimeout(
            hwnd,
            msg,
            wParam,
            lParam,
            NativeMethods.SMTO_ABORTIFHUNG,
            TimeoutMs,
            out _);
}

/// <summary>Pure builder for the mouse-message sequence replayed onto a tray button.</summary>
internal static class TrayClickPlan
{
    internal static IntPtr PackLParam(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    internal static IReadOnlyList<(uint Message, IntPtr WParam, IntPtr LParam)> Build(TrayClick kind, int x, int y)
    {
        uint mods = GetModifiers();
        IntPtr lParam = PackLParam(x, y);
        return kind switch
        {
            TrayClick.Right => new (uint, IntPtr, IntPtr)[]
            {
                (NativeMethods.WM_RBUTTONDOWN, (IntPtr)(NativeMethods.MK_RBUTTON | mods), lParam),
                ((uint)NativeMethods.WM_RBUTTONUP, (IntPtr)mods, lParam),
            },
            TrayClick.DoubleLeft => new (uint, IntPtr, IntPtr)[]
            {
                (NativeMethods.WM_LBUTTONDOWN, (IntPtr)(NativeMethods.MK_LBUTTON | mods), lParam),
                ((uint)NativeMethods.WM_LBUTTONUP, (IntPtr)mods, lParam),
                (NativeMethods.WM_LBUTTONDBLCLK, (IntPtr)(NativeMethods.MK_LBUTTON | mods), lParam),
                ((uint)NativeMethods.WM_LBUTTONUP, (IntPtr)mods, lParam),
            },
            _ => new (uint, IntPtr, IntPtr)[]
            {
                (NativeMethods.WM_LBUTTONDOWN, (IntPtr)(NativeMethods.MK_LBUTTON | mods), lParam),
                ((uint)NativeMethods.WM_LBUTTONUP, (IntPtr)mods, lParam),
            },
        };
    }

    private static uint GetModifiers()
    {
        uint mods = 0;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0)
        {
            mods |= NativeMethods.MK_SHIFT;
        }

        if ((NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0)
        {
            mods |= NativeMethods.MK_CONTROL;
        }

        return mods;
    }
}