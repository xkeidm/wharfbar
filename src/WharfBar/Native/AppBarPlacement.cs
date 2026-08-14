namespace WharfBar.Native;

/// <summary>
/// Pure placement decisions for the top AppBar. Calling <c>ABM_SETPOS</c> on every
/// <c>ABN_POSCHANGED</c> retriggers the same notification (and can deadlock if done
/// inside explorer's SendMessage), which leaves the bar showing the wait cursor.
/// </summary>
internal static class AppBarPlacement
{
    internal static bool RectsEqual(in RECT a, in RECT b) =>
        a.Left == b.Left
        && a.Top == b.Top
        && a.Right == b.Right
        && a.Bottom == b.Bottom;

    /// <summary>
    /// True when the shell must see <c>ABM_SETPOS</c>. False when QUERYPOS returned the
    /// same rect we already committed — a no-op SETPOS would loop with ABN_POSCHANGED.
    /// </summary>
    internal static bool NeedsShellSetPos(bool haveLastApproved, in RECT lastApproved, in RECT approved) =>
        !haveLastApproved || !RectsEqual(lastApproved, approved);

    /// <summary>
    /// True when the hidden taskbar AppBar has reclaimed the bottom strip
    /// (<c>SPI_GETWORKAREA</c> bottom sits above the monitor edge).
    /// </summary>
    internal static bool NeedsBottomExpand(int workBottom, int monitorBottom) =>
        workBottom < monitorBottom;

    /// <summary>
    /// Keep-hidden tray visibility poll. Fast enough to catch explorer re-showing
    /// <c>Shell_TrayWnd</c>, slow enough not to crowd the WPF dispatcher.
    /// </summary>
    internal const int KeepHiddenTrayPollMs = 250;

    /// <summary>
    /// Work-area expand on the keep-hidden watch. <c>SPI_GETWORKAREA</c> every 100 ms
    /// plus <c>ABM_WINDOWPOSCHANGED</c> from the slide clamp stalls the UI thread
    /// (stuttering dock auto-hide, delayed top-bar menus). ApplyHide / AppBar SETPOS
    /// still expand immediately; this is only a backup for a silent phantom strip.
    /// </summary>
    internal const int KeepHiddenWorkAreaPollMs = 1000;

    /// <summary>
    /// True when <paramref name="lastMs"/> never ran (< 0) or the interval has elapsed.
    /// </summary>
    internal static bool IsIntervalDue(long nowMs, long lastMs, int intervalMs) =>
        lastMs < 0 || nowMs - lastMs >= intervalMs;

    /// <summary>
    /// Skip <c>ABM_WINDOWPOSCHANGED</c> when we already sit on the approved AppBar rect.
    /// WPF keeps proposing a work-area shove (<c>y=30</c>); the clamp rejects it, then
    /// a shell notify would SendMessage explorer on every layout and freeze the UI.
    /// </summary>
    internal static bool ShouldNotifyShellWindowPosChanged(
        bool isApplyingPlacement,
        bool haveApproved,
        in RECT current,
        in RECT approved) =>
        !isApplyingPlacement && !(haveApproved && RectsEqual(current, approved));

    /// <summary>
    /// Auto-hide dock clamp range from cached visible/hidden Y — callers must not
    /// <c>Measure</c> the visual tree inside <c>WM_WINDOWPOSCHANGING</c>.
    /// </summary>
    internal static void GetDockSlideClampRange(
        bool autoHideEnabled,
        bool allowSlide,
        bool parkedHidden,
        int visibleY,
        int hiddenY,
        out int minY,
        out int maxY)
    {
        if (!autoHideEnabled)
        {
            minY = maxY = visibleY;
            return;
        }

        if (allowSlide)
        {
            minY = Math.Min(visibleY, hiddenY);
            maxY = Math.Max(visibleY, hiddenY);
            return;
        }

        int parked = parkedHidden ? hiddenY : visibleY;
        minY = maxY = parked;
    }

    /// <summary>
    /// Keeps an overlay dock's Y inside the auto-hide slide range. Explorer/WPF shove the
    /// window up into the work area when the phantom taskbar strip returns (~48px).
    /// </summary>
    internal static int ClampSlideY(int y, int visibleY, int hiddenY)
    {
        int min = Math.Min(visibleY, hiddenY);
        int max = Math.Max(visibleY, hiddenY);
        return Math.Clamp(y, min, max);
    }

    /// <summary>
    /// Rewrites a <c>WINDOWPOS</c> proposal so the HWND stays on <paramref name="approved"/>.
    /// When the phantom taskbar reservation returns, WPF treats y=0 as outside the work area
    /// and moves the AppBar to work-area top — a frozen copy of the bar remains in the
    /// reserved strip ("bar under bar").
    /// </summary>
    internal static bool TryAlignToRect(
        int x,
        int y,
        int cx,
        int cy,
        uint flags,
        int currentX,
        int currentY,
        int currentW,
        int currentH,
        in RECT approved,
        out int alignedX,
        out int alignedY,
        out int alignedCx,
        out int alignedCy,
        out uint alignedFlags)
    {
        bool moving = (flags & NativeMethods.SWP_NOMOVE) == 0;
        bool sizing = (flags & NativeMethods.SWP_NOSIZE) == 0;

        int effectiveX = moving ? x : currentX;
        int effectiveY = moving ? y : currentY;
        int effectiveW = sizing ? cx : currentW;
        int effectiveH = sizing ? cy : currentH;

        if (effectiveX == approved.Left
            && effectiveY == approved.Top
            && effectiveW == approved.Width
            && effectiveH == approved.Height)
        {
            alignedX = x;
            alignedY = y;
            alignedCx = cx;
            alignedCy = cy;
            alignedFlags = flags;
            return false;
        }

        alignedX = approved.Left;
        alignedY = approved.Top;
        alignedCx = approved.Width;
        alignedCy = approved.Height;
        alignedFlags = flags;
        if (approved.Left != currentX || approved.Top != currentY)
        {
            alignedFlags &= ~NativeMethods.SWP_NOMOVE;
        }

        if (approved.Width != currentW || approved.Height != currentH)
        {
            alignedFlags &= ~NativeMethods.SWP_NOSIZE;
        }

        return true;
    }
}
