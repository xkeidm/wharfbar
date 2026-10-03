namespace WharfBar.Native;

/// <summary>
/// Thin wrappers for primary-monitor taskbar hwnd + AppBar state.
/// UI must not call NativeMethods for tray hide/restore directly.
/// </summary>
internal static class TaskbarNative
{
    private const string TrayClass = "Shell_TrayWnd";
    private const string SecondaryTrayClass = "Shell_SecondaryTrayWnd";

    /// <summary>DWMWA_CLOAK — hide from composition while the HWND stays "shown" for UIA.</summary>
    private const uint DwmwaCloak = 13;

    private static RECT _savedTrayRect;
    private static bool _haveSavedTrayRect;
    private static bool _trayParkedOffscreen;
    private static bool _trayCloakedForStealth;

    /// <summary>
    /// Original GWL_EXSTYLE of every tray hwnd whose stealth style (WS_EX_LAYERED |
    /// WS_EX_TRANSPARENT, alpha 0) WharfBar applied. Styles persist for the whole run:
    /// showing the primary bar makes explorer async-sync-show secondary bars even AFTER a
    /// peek ended, so restoring styles per peek would let those late shows paint visibly.
    /// With persistent styles any shell-driven show stays invisible; full visibility is
    /// restored only for explicit user-facing peeks and at exit.
    /// </summary>
    private static readonly Dictionary<IntPtr, int> _stealthOriginalExStyles = new();

    internal static IntPtr FindTrayHwnd() =>
        NativeMethods.FindWindow(TrayClass, null);

    internal static uint GetState()
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
        };
        return (uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
    }

    internal static void SetState(uint state)
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            lParam = (IntPtr)(int)state,
        };
        _ = NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
    }

    internal static bool IsTrayVisible()
    {
        IntPtr hwnd = FindTrayHwnd();
        return hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd);
    }

    /// <summary>
    /// True when any primary or secondary taskbar HWND is visible. Explorer can re-show
    /// <c>Shell_TrayWnd</c> after AppBar <c>SETPOS</c> or auto-hide edge reveal.
    /// </summary>
    internal static bool IsAnyTrayVisible()
    {
        bool visible = false;
        ForEachTrayHwnd(hwnd =>
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                visible = true;
            }
        });
        return visible;
    }

    internal static void SetTrayVisible(bool visible)
    {
        IntPtr primary = FindTrayHwnd();
        if (primary != IntPtr.Zero)
        {
            if (visible)
            {
                ClearStealthVisuals(primary);
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("WharfBar: Shell_TrayWnd not found");
        }

        int shown = 0;
        ForEachTrayHwnd(hwnd =>
        {
            shown++;
            ApplyTrayVisibility(hwnd, visible);
            ClearStealthGeometry(hwnd);
            if (visible)
            {
                // Full visibility is only wanted on explicit user-facing peeks / exit;
                // the hide path keeps persistent stealth styles in place.
                ClearStealthVisuals(hwnd);
            }
        });

        if (shown == 0)
        {
            System.Diagnostics.Debug.WriteLine("WharfBar: no Shell_TrayWnd / Shell_SecondaryTrayWnd");
        }
    }

    /// <summary>
    /// Physical-pixel gap between the primary work-area bottom and the monitor bottom —
    /// the taskbar's (or another appbar's) bottom reservation. >0 while the hidden
    /// taskbar's phantom AppBar registration still holds the strip.
    /// </summary>
    internal static bool TryGetPrimaryWorkAreaGapPx(out int gapPx)
    {
        gapPx = 0;
        IntPtr tray = FindTrayHwnd();
        IntPtr anchor = tray != IntPtr.Zero ? tray : NativeMethods.GetDesktopWindow();
        IntPtr monitor = NativeMethods.MonitorFromWindow(anchor, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        gapPx = info.rcMonitor.Bottom - info.rcWork.Bottom;
        return gapPx >= 0;
    }

    /// <summary>
    /// With the auto-hide STATE on (strip release, ADR-005 note 5) explorer parks
    /// Shell_TrayWnd at a ~2px bottom sliver and clamps every move; the click path drops
    /// the state first, which releases the clamp, and this then slides the (alpha-0
    /// invisible) tray fully on-screen so forwarded clicks land on real coordinates.
    /// </summary>
    internal static void EnsureTrayOnScreen(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out RECT rect) || rect.Height <= 0)
        {
            return;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        int parkedTop = info.rcMonitor.Bottom - 2;
        if (rect.Top < parkedTop)
        {
            return; // not parked
        }

        _ = NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            info.rcMonitor.Left,
            info.rcMonitor.Bottom - rect.Height,
            rect.Width,
            rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private static void ApplyTrayVisibility(IntPtr hwnd, bool visible)
    {
        if (visible)
        {
            // SW_SHOW (not SW_SHOWNA): after SW_HIDE, SHOWNA often leaves Shell_TrayWnd invisible.
            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            _ = NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_SHOWWINDOW
                    | NativeMethods.SWP_NOMOVE
                    | NativeMethods.SWP_NOSIZE
                    | NativeMethods.SWP_NOZORDER
                    | NativeMethods.SWP_NOACTIVATE);
            EnsureTrayOnScreen(hwnd);
            return;
        }

        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
        _ = NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_HIDEWINDOW
                | NativeMethods.SWP_NOMOVE
                | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER
                | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Enumerates only <c>Shell_SecondaryTrayWnd</c> windows (multi-monitor bars).</summary>
    private static void ForEachSecondaryTrayHwnd(Action<IntPtr> action)
    {
        IntPtr secondary = IntPtr.Zero;
        while (true)
        {
            secondary = NativeMethods.FindWindowEx(IntPtr.Zero, secondary, SecondaryTrayClass, null);
            if (secondary == IntPtr.Zero)
            {
                break;
            }

            action(secondary);
        }
    }

    /// <summary>
    /// Primary via <c>FindWindow</c>, secondaries via <c>FindWindowEx</c>. Avoid
    /// <c>EnumWindows</c> on the UI thread — a hung top-level window stalls the callback
    /// and the AppBar shows the wait cursor.
    /// </summary>
    private static void ForEachTrayHwnd(Action<IntPtr> action)
    {
        IntPtr primary = FindTrayHwnd();
        if (primary != IntPtr.Zero)
        {
            action(primary);
        }

        IntPtr secondary = IntPtr.Zero;
        while (true)
        {
            secondary = NativeMethods.FindWindowEx(
                IntPtr.Zero,
                secondary,
                SecondaryTrayClass,
                null);
            if (secondary == IntPtr.Zero)
            {
                break;
            }

            action(secondary);
        }
    }

    /// <summary>
    /// Shows <c>Shell_TrayWnd</c> for UIA without a visible flash. Cloak path: DWM cloak +
    /// park below the primary monitor, then <c>SW_SHOW</c>. Alpha path (cloak refused):
    /// WS_EX_LAYERED|WS_EX_TRANSPARENT at alpha 0, shown in place without any move — moving
    /// makes explorer re-sync secondary taskbars (visible blink each poll). Pair with
    /// <see cref="SetTrayVisible"/>(false). Returns false when neither invisibility
    /// mechanism applies; the caller must skip this peek.
    /// </summary>
    internal static bool TrySetTrayVisibleStealth()
    {
        IntPtr hwnd = FindTrayHwnd();
        if (hwnd == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("WharfBar: Shell_TrayWnd not found (stealth)");
            return false;
        }

        // Cloak first so even a momentary shell snap to the bottom edge is not painted.
        bool useAlpha = !TrySetCloaked(hwnd, cloaked: true);
        _trayCloakedForStealth = !useAlpha;

        if (useAlpha)
        {
            // This machine refuses DWM cloak on Shell_TrayWnd. Fall back to persistent
            // stealth styling: WS_EX_LAYERED | WS_EX_TRANSPARENT at alpha 0 on every tray
            // window. IsWindowVisible stays true so UIA keeps exposing the tray buttons,
            // yet nothing composites — including explorer's async sync-show of secondary
            // bars, which can land even AFTER the peek ended (restoring styles per peek
            // let those late shows blink visibly). Do NOT move or resize anything here:
            // a SetWindowPos alone provokes the same resync.
            if (!EnsureTrayStealthStyle(hwnd))
            {
                TrayPeekDiagnostics.Log("stealth-skip-no-invisibility", hwnd, false);
                return false;
            }

            ForEachSecondaryTrayHwnd(h => _ = EnsureTrayStealthStyle(h));

            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            TrayPeekDiagnostics.Log("stealth-show-alpha", hwnd, false);
            return true;
        }

        if (NativeMethods.GetWindowRect(hwnd, out RECT rect) && rect.Width > 0 && rect.Height > 0)
        {
            if (!_trayParkedOffscreen)
            {
                _savedTrayRect = rect;
                _haveSavedTrayRect = true;
            }

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
            var info = new MONITORINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
            };
            if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                _ = NativeMethods.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    info.rcMonitor.Left,
                    info.rcMonitor.Bottom + 8,
                    rect.Width,
                    rect.Height,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                _trayParkedOffscreen = true;
            }
        }

        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);

        TrayPeekDiagnostics.Log("stealth-show", hwnd, _trayCloakedForStealth);
        return true;
    }

    /// <summary>Restores full tray visibility (un-park / uncloak / un-alpha; peek upgrade).</summary>
    internal static void UnparkTrayOnScreen()
    {
        IntPtr hwnd = FindTrayHwnd();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        ForEachTrayHwnd(ClearStealthVisuals);
        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
    }

    /// <summary>
    /// Invisible-anchored click session: drops WS_EX_TRANSPARENT from the primary tray
    /// (keeps LAYERED) and lifts its alpha to 1 so the unseen parked bar still receives
    /// the synthetic mouse input at its coordinates. Layered-window hit testing follows
    /// alpha: an alpha of 0 lets mouse messages pass through even without WS_EX_TRANSPARENT,
    /// while any non-zero alpha (1 ≈ 0.4%) is optically invisible but hit-testable.
    /// <see cref="EnsureTrayStealthStyles"/> restores WS_EX_TRANSPARENT and alpha 0 on the
    /// next hide cycle.
    /// </summary>
    internal static void BeginTrayClickInput()
    {
        IntPtr hwnd = FindTrayHwnd();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0)
        {
            _ = NativeMethods.SetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE,
                exStyle & ~NativeMethods.WS_EX_TRANSPARENT);
        }

        _ = NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 1, NativeMethods.LWA_ALPHA);
    }

    /// <summary>Reapplies full stealth styling after a click session.</summary>
    internal static void EndTrayClickInput() => EnsureTrayStealthStyles();

    /// <summary>
    /// Applies persistent stealth styling (layered + click-through + alpha 0) to every tray
    /// hwnd present right now, including newly created secondaries. Idempotent.
    /// </summary>
    internal static void EnsureTrayStealthStyles()
    {
        ForEachTrayHwnd(h => _ = EnsureTrayStealthStyle(h));
    }

    private static bool EnsureTrayStealthStyle(IntPtr hwnd)
    {
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        const int layeredTransparent =
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT;
        if (!_stealthOriginalExStyles.TryGetValue(hwnd, out int original))
        {
            original = exStyle;
            _stealthOriginalExStyles[hwnd] = original;
        }

        if ((exStyle & layeredTransparent) != layeredTransparent)
        {
            _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | layeredTransparent);
            if ((NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE) & layeredTransparent)
                != layeredTransparent)
            {
                return false;
            }
        }

        return NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA);
    }

    private static void ClearStealthGeometry(IntPtr hwnd)
    {
        if (_trayParkedOffscreen && _haveSavedTrayRect)
        {
            _ = NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                _savedTrayRect.Left,
                _savedTrayRect.Top,
                _savedTrayRect.Width,
                _savedTrayRect.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            _trayParkedOffscreen = false;
        }

        if (_trayCloakedForStealth)
        {
            _ = TrySetCloaked(hwnd, cloaked: false);
            _trayCloakedForStealth = false;
        }
    }

    /// <summary>Geometry cleanup plus full opacity/style restoration for one hwnd.</summary>
    private static void ClearStealthVisuals(IntPtr hwnd)
    {
        ClearStealthGeometry(hwnd);

        // Alpha restore runs while the window is still layered, then the bits drop —
        // every intermediate state stays fully opaque. The stealth bits are stripped
        // explicitly rather than trusting the captured "original": a previous run that
        // died (hard kill) left them applied, so the captured original can itself be
        // poisoned (restore would then leave a visible-but-click-through taskbar).
        if (_stealthOriginalExStyles.TryGetValue(hwnd, out int exStyle))
        {
            _ = NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
            _ = NativeMethods.SetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE,
                exStyle & ~(NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT));
        }
    }

    private static bool TrySetCloaked(IntPtr hwnd, bool cloaked)
    {
        int value = cloaked ? 1 : 0;
        return NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaCloak, ref value, sizeof(int))
            == NativeMethods.S_OK;
    }

    /// <summary>
    /// Primary monitor work area via <c>SPI_GETWORKAREA</c> (physical pixels).
    /// </summary>
    internal static bool TryGetPrimaryWorkArea(out RECT work)
    {
        work = default;
        return NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_GETWORKAREA,
            0,
            ref work,
            0);
    }

    /// <summary>
    /// Expands the primary work-area bottom to the monitor edge (minus the optional dock
    /// reservation strip) while keeping top/left/right (top AppBar reservation).
    /// <c>ShowWindow(SW_HIDE)</c> alone leaves a phantom taskbar strip.
    /// </summary>
    /// <param name="reservedBottomPx">Strip reserved by the dock (0 = none).</param>
    /// <param name="broadcast">
    /// When true, writes with <c>SPIF_SENDCHANGE</c>. A broadcast makes the shell recompute
    /// appbar reservations and re-asserts the hidden taskbar's phantom strip, which then
    /// needs another expansion — an endless fight. Ongoing maintenance must be silent.
    /// </param>
    internal static bool TryExpandPrimaryWorkAreaBottom(int reservedBottomPx = 0, bool broadcast = false)
    {
        if (!WorkAreaExpandPolicy.Shared.ShouldAttempt(DateTimeOffset.UtcNow))
        {
            return true; // known-broken SPI on this shell; treat as handled no-op
        }

        IntPtr tray = FindTrayHwnd();
        IntPtr anchor = tray != IntPtr.Zero ? tray : NativeMethods.GetDesktopWindow();

        IntPtr monitor = NativeMethods.MonitorFromWindow(anchor, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        if (!TryGetPrimaryWorkArea(out RECT work) || work.Width <= 0 || work.Height <= 0)
        {
            work = info.rcWork;
        }

        int targetBottom = info.rcMonitor.Bottom - Math.Max(0, reservedBottomPx);
        if (!AppBarPlacement.NeedsBottomExpand(work.Bottom, targetBottom))
        {
            return true;
        }

        var expanded = new RECT
        {
            Left = work.Left,
            Top = work.Top,
            Right = work.Right,
            Bottom = targetBottom,
        };

        return RecordSetWorkArea(NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_SETWORKAREA,
            0,
            ref expanded,
            broadcast ? NativeMethods.SPIF_SENDCHANGE : 0), targetBottom, expanded);
    }

    /// <summary>
    /// Expands <paramref name="monitor"/>'s work-area bottom to its edge (minus an optional dock
    /// reservation strip). SPI_SETWORKAREA applies to the monitor containing the rect. Silent
    /// (no SPIF_SENDCHANGE) except when <paramref name="broadcast"/> is true.
    /// </summary>
    /// <remarks>
    /// Yalnız birincil monitör devre kesicisini besler (<paramref name="feedsBreaker"/> =
    /// <c>false</c> buradan). Aksi halde ikincil monitörün genişletmesi, ki o monitörün
    /// work-area'sı doğru okunsa bile "etki uygulanmadı" sayılabilecek tek bir çağrıyla devreyi
    /// 10 dakika açık tutup <see cref="TrySetPrimaryWorkArea"/>'ı — yani çıkıştaki work-area
    /// geri yüklemesini — erken çıkmaya zorlayabilirdi. Bkz. <c>boundaries.md</c> "Asla".
    /// </remarks>
    internal static bool TryExpandWorkAreaBottom(
        MonitorInfo monitor,
        int reservedBottomPx,
        bool broadcast,
        bool feedsBreaker)
    {
        if (!WorkAreaExpandPolicy.Shared.ShouldAttempt(DateTimeOffset.UtcNow))
        {
            return true; // known-broken SPI on this shell; treat as handled no-op
        }

        RECT work = monitor.WorkArea;
        if (work.Width <= 0 || work.Height <= 0)
        {
            return false;
        }

        int targetBottom = monitor.Bounds.Bottom - Math.Max(0, reservedBottomPx);
        if (!AppBarPlacement.NeedsBottomExpand(work.Bottom, targetBottom))
        {
            return true;
        }

        var expanded = new RECT
        {
            Left = work.Left,
            Top = work.Top,
            Right = work.Right,
            Bottom = targetBottom,
        };

        return RecordSetWorkArea(NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_SETWORKAREA,
            0,
            ref expanded,
            broadcast ? NativeMethods.SPIF_SENDCHANGE : 0), targetBottom, expanded, feedsBreaker);
    }

    /// <summary>Restores a previously captured primary work area (exit / crash restore).</summary>
    /// <remarks>
    /// Devre kesicinin "SPI bu shell'de ölü" dediği durumda bile <b>denenir</b>: devre
    /// kesicisi yalnız genişletme yolunu susturur, restore yolunu susturmaz. Çıkışta tek bir
    /// ölü çağrının bedeli yoktur, ama kesik bir restore'un bedeli alt boşlukta kalan bir
    /// görev çubuğudur — <c>boundaries.md</c> "Asla" listesi. 25H2'de ölü olan API
    /// <c>TRUE</c> döndüp hiçbir şey yapmadığı için (ölçüldü) bu çağrı zararsızdır.
    /// </remarks>
    internal static bool TrySetPrimaryWorkArea(RECT work)
    {
        if (work.Width <= 0 || work.Height <= 0)
        {
            return false;
        }

        return RecordSetWorkArea(NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_SETWORKAREA,
            0,
            ref work,
            NativeMethods.SPIF_SENDCHANGE), work.Bottom, work);
    }

    /// <summary>
    /// Feeds the call outcome into the circuit breaker. The API's return value is not trusted:
    /// 25H2 also answers <c>TRUE</c> while changing nothing, so the work area is read back and
    /// compared against <paramref name="expectedBottom"/>. Only a call that actually moved the
    /// edge counts as success; the first dead-API result per circuit-open transition is surfaced
    /// in debug output.
    /// </summary>
    /// <param name="written">The rect handed to <c>SPI_SETWORKAREA</c> — it identifies the
    /// monitor the write applied to, which is the monitor the read-back must come from.</param>
    /// <param name="feedsBreaker">False for calls that must not be able to open the circuit
    /// (see <see cref="TryExpandWorkAreaBottom"/>).</param>
    private static bool RecordSetWorkArea(bool ok, int expectedBottom, RECT written, bool feedsBreaker = true)
    {
        int error = ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        bool readBackOk = TryReadBackWorkAreaBottom(written, out int readBackBottom);
        return RecordSetWorkAreaResult(
            ok,
            error,
            readBackOk,
            readBackBottom,
            expectedBottom,
            feedsBreaker,
            DateTimeOffset.UtcNow,
            WorkAreaExpandPolicy.Shared);
    }

    /// <summary>
    /// Reads the bottom edge of the work area of the monitor that <paramref name="written"/>
    /// belongs to (<c>MonitorFromRect</c> + <c>GetMonitorInfo.rcWork</c>).
    /// </summary>
    /// <remarks>
    /// <c>SPI_GETWORKAREA</c> <b>yalnız birincil</b> monitörü okur (uiParam = 0), ama
    /// <c>SPI_SETWORKAREA</c> yazılan dikdörtgenin içinde bulunduğu monitöre uygulanır ve
    /// <see cref="TryExpandWorkAreaBottom"/> her monitör için çağrılır. Birincil okuma
    /// ikincinin başarılı genişlemesini "etki uygulanmadı" sayar; iki hata sonrası devre
    /// kesicisi açılır ve çıkıştaki restore work area'yı geri yükleyemez. Monitör çözülemezse
    /// birincil okumaya düşülür — bu yol yalnız birincil dikdörtgenler için doğrudur.
    /// </remarks>
    private static bool TryReadBackWorkAreaBottom(RECT written, out int bottom)
    {
        IntPtr monitor = NativeMethods.MonitorFromRect(ref written, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
            };
            if (NativeMethods.GetMonitorInfo(monitor, ref info) && info.rcWork.Height > 0)
            {
                bottom = info.rcWork.Bottom;
                return true;
            }
        }

        if (TryGetPrimaryWorkArea(out RECT primaryWork))
        {
            bottom = primaryWork.Bottom;
            return true;
        }

        bottom = 0;
        return false;
    }

    /// <summary>
    /// The verdict, the breaker feed and the one-shot debug line, split from the Win32
    /// round-trip so the breaker's gating is testable without a live shell.
    /// </summary>
    internal static bool RecordSetWorkAreaResult(
        bool ok,
        int error,
        bool readBackOk,
        int readBackBottom,
        int expectedBottom,
        bool feedsBreaker,
        DateTimeOffset now,
        WorkAreaExpandPolicy policy)
    {
        bool effectApplied = ok && readBackOk && readBackBottom == expectedBottom;

        if (feedsBreaker)
        {
            bool wasOpen = policy.IsCircuitOpen(now);
            policy.RecordResult(ok, error, effectApplied, now);
            if (policy.IsCircuitOpen(now) && !wasOpen)
            {
                System.Diagnostics.Debug.WriteLine(
                    ok
                        ? "WharfBar: SPI_SETWORKAREA reported success but the work area did not move — " +
                          "Windows 11 25H2 does not support work-area overrides; the hidden taskbar's " +
                          "bottom strip stays reserved. Maximized windows will respect the strip."
                        : $"WharfBar: SPI_SETWORKAREA rejected (Win32 error {error}) — Windows 11 25H2 " +
                          "does not support work-area overrides; the hidden taskbar's bottom strip " +
                          "cannot be released. Maximized windows will respect the strip.");
            }
        }

        return ok && effectApplied;
    }
}
