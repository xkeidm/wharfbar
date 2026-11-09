namespace WharfBar.Services;

/// <summary>
/// What the top bar can honestly say about the tray mirror (ADR-009 hybrid: only icons that are
/// visible on the taskbar are mirrored; Windows' hidden/overflow icons stay behind <c>Win+B</c>).
/// The bar used to collapse the mirror area with no explanation at all — an empty tray, a tray
/// that could not be read yet and a tray with every icon in the hidden area all looked identical.
/// </summary>
internal enum TrayMirrorState
{
    /// <summary>No snapshot yet and no read has proven the tray empty: still reading.</summary>
    Unread,

    /// <summary>The mirror has icons; they are drawn on the bar.</summary>
    Populated,

    /// <summary>
    /// The tray was verifiably shown and read back with <b>no app icons</b>. Legitimate: every
    /// third-party icon lives in Windows' hidden area, which the mirror must not read (ADR-009).
    /// </summary>
    TrayConfirmedEmpty,
}

/// <summary>Chevron tooltip variant for the current mirror state.</summary>
internal enum TrayOverflowTooltip
{
    /// <summary>Existing copy ("Hidden icons"): there is something on the bar or in the pill.</summary>
    Default,

    /// <summary>Tray verified empty — tell the user their icons are in Windows' hidden area (Win+B).</summary>
    NoVisibleIcons,

    /// <summary>Nothing has been read yet — say so instead of showing a blank bar.</summary>
    Unread,
}

/// <summary>
/// Pure mapping between the mirror's last read and what the bar shows/tells. Split out so the
/// "empty mirror must not be silent" rule is testable without a window.
/// </summary>
internal static class TrayMirrorStates
{
    /// <summary>
    /// State for a published snapshot. Only <see cref="TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty"/>
    /// (a tray that was verified fully on screen and read back empty) may claim the tray is empty;
    /// every other empty result means "not read yet" and must not be presented as a fact.
    /// </summary>
    internal static TrayMirrorState For(int publishedCount, TrayMirrorService.TrayBlipOutcome outcome) =>
        publishedCount > 0
            ? TrayMirrorState.Populated
            : outcome == TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty
                ? TrayMirrorState.TrayConfirmedEmpty
                : TrayMirrorState.Unread;

    /// <summary>
    /// <paramref name="visibleIconCount"/> icons are mirrored onto the bar,
    /// <paramref name="mirrorItemCount"/> were read in total (the rest are parked in the Hidden
    /// icons pill by the user, which is intentional). Only a mirror with nothing on the bar and
    /// nothing parked earns an explanatory tooltip.
    /// </summary>
    internal static TrayOverflowTooltip TooltipFor(
        int visibleIconCount,
        int mirrorItemCount,
        TrayMirrorState state)
    {
        if (visibleIconCount > 0 || mirrorItemCount > 0)
        {
            return TrayOverflowTooltip.Default;
        }

        return state == TrayMirrorState.TrayConfirmedEmpty
            ? TrayOverflowTooltip.NoVisibleIcons
            : TrayOverflowTooltip.Unread;
    }
}
