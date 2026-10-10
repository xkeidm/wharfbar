using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>How a mirrored tray icon click is replayed onto the real toolbar.</summary>
internal enum TrayClick
{
    Left,
    Right,
    DoubleLeft,
}

/// <summary>
/// Which mirrored gesture is replayed onto the real tray button. Right-click menus open on
/// button-up (<c>WM_RBUTTONUP</c> / <c>WM_CONTEXTMENU</c>). Forwarding the right button on
/// down injects that click while the top bar still has mouse capture, so the tray button
/// never sees the up and the owner's menu does not open. Left clicks stay on down.
/// </summary>
internal static class TrayGestureMap
{
    internal static bool TryForward(bool isRightButton, int clickCount, bool isRelease, out TrayClick kind)
    {
        if (isRightButton)
        {
            kind = TrayClick.Right;
            return isRelease;
        }

        if (isRelease)
        {
            kind = default;
            return false;
        }

        kind = clickCount >= 2 ? TrayClick.DoubleLeft : TrayClick.Left;
        return true;
    }
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
    private struct TBBUTTON
    {
        public int iBitmap;
        public int idCommand;
        public byte fsState;
        public byte fsStyle;
        public byte bReserved0;
        public byte bReserved1;
        public IntPtr dwData;
        public IntPtr iString;
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

    /// <summary>Button rect in screen pixels (client rect mapped with <c>ClientToScreen</c>).</summary>
    internal static bool TryGetItemScreenRect(IntPtr toolbar, int index, out RECT screen)
    {
        screen = default;
        if (!TryGetItemRect(toolbar, index, out RECT client))
        {
            return false;
        }

        var topLeft = new POINT { X = client.Left, Y = client.Top };
        var bottomRight = new POINT { X = client.Right, Y = client.Bottom };
        if (!ClientToScreen(toolbar, ref topLeft) || !ClientToScreen(toolbar, ref bottomRight))
        {
            return false;
        }

        screen = new RECT
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = bottomRight.X,
            Bottom = bottomRight.Y,
        };
        return screen.Width > 0 && screen.Height > 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    internal static void ClickItem(IntPtr toolbar, int index, TrayClick kind)
    {
        if (!TryGetItemRect(toolbar, index, out RECT rect))
        {
            return;
        }

        // The icon owner calls TrackPopupMenu from WM_RBUTTONUP / WM_CONTEXTMENU. That
        // fails unless the process which just received the user's click grants foreground.
        // The shell builds the notify from these mouse messages; we do not pack NIN_* .
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

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

        IntPtr rawPtr = Marshal.AllocHGlobal(Marshal.SizeOf<TBBUTTON>());
        try
        {
            var raw = new TBBUTTON();
            Marshal.StructureToPtr(raw, rawPtr, false);
            IntPtr ret = SendTimeout(toolbar, NativeMethods.TB_GETBUTTON, (IntPtr)index, rawPtr);
            if (ret.ToInt32() == 0)
            {
                return false;
            }

            raw = Marshal.PtrToStructure<TBBUTTON>(rawPtr);
            // TB_GETBUTTONTEXT wParam is the COMMAND ID (MSDN), not the index.
            string tooltip = ReadTooltip(toolbar, raw.idCommand);
            info = new TrayButtonInfo(raw.idCommand, raw.iBitmap, tooltip);
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