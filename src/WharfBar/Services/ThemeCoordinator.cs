using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WharfBar.Services;

/// <summary>
/// Owns "the app theme changed" as one coordinated switch: one dictionary apply, one crossfade,
/// one re-assert of every shell surface.
///
/// Two entry points exist:
/// <list type="bullet">
/// <item>in-app (Appearance page / setup wizard): <see cref="AppearanceApplier"/> calls
/// <see cref="ThemeTransition"/> directly, because it also persists the choice;</item>
/// <item>Windows broadcast (system theme with <c>theme=system</c>, colourization): every top-level
/// window receives the message. The first one schedules the switch; the others are suppressed while
/// the coordinator owns the transition instead of racing it with their own partial re-applies.</item>
/// </list>
///
/// The broadcast is scheduled instead of handled inside the window procedure: Windows sends
/// <c>WM_SETTINGCHANGE</c> synchronously and waits for the window, so doing the dictionary swap and
/// the full shell re-assert inside that message blocked the sender (the Settings app / ours) and
/// repainted windows in the middle of the message pump.
/// </summary>
internal static class ThemeCoordinator
{
    private static bool _systemSwitchScheduled;

    /// <summary>True while a system-driven switch is queued on the dispatcher.</summary>
    internal static bool IsTransitionPending => _systemSwitchScheduled;

    /// <summary>
    /// True while a window's own theme re-assert should be skipped: a queued system switch or the
    /// in-app transition already re-asserted every surface.
    /// </summary>
    internal static bool SuppressReactiveRefresh =>
        _systemSwitchScheduled || ThemeTransition.IsSuppressingReactiveRefresh;

    /// <summary>
    /// Schedules the coordinated switch when the resolved theme differs from the applied one.
    /// Returns true when the caller should leave the re-assert to the coordinator.
    /// </summary>
    internal static bool TryScheduleSystemThemeChange()
    {
        ApplicationTheme desired = AppThemePreference.Resolve();
        if (ThemeApplyGuard.IsCurrent(desired))
        {
            return false;
        }

        if (_systemSwitchScheduled)
        {
            return true;
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return false;
        }

        _systemSwitchScheduled = true;
        _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            _systemSwitchScheduled = false;

            ApplicationTheme target = AppThemePreference.Resolve();
            if (ThemeApplyGuard.IsCurrent(target))
            {
                return;
            }

            RunSystemSwitch(target);
        }));

        return true;
    }

    private static void RunSystemSwitch(ApplicationTheme target)
    {
        ThemeTransition.Run(
            () =>
            {
                // Backdrop None: each window re-asserts its own Mica/tint/transparent surface right
                // below, and a Mica attach to the layered (transparent/tint) top bar is forbidden.
                if (!ThemeApplyGuard.Apply(target, WindowBackdropType.None, updateAccent: false))
                {
                    return;
                }

                (Application.Current as App)?.ApplyThemeChanges();
            },
            // The wallpaper partner fade is itself a full-screen transition; start it only after the
            // shell crossfade, never on top of it.
            () => (Application.Current as App)?.FollowWallpaperTheme(fromExternalWindows: true));
    }
}
