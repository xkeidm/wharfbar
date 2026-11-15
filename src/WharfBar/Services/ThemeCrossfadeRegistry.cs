using System.Windows;

namespace WharfBar.Services;

/// <summary>
/// Windows that take part in the light/dark crossfade register the visual whose content is captured
/// before the palette swaps. Registration is explicit (a service has no reason to type-switch over
/// views) and holds only a weak reference, so a closed window can neither leak nor keep a stale
/// surface in the list.
/// </summary>
internal static class ThemeCrossfadeRegistry
{
    private sealed class Entry
    {
        internal Entry(WeakReference<Window> window, Func<FrameworkElement?> visual)
        {
            Window = window;
            Visual = visual;
        }

        internal WeakReference<Window> Window { get; }

        internal Func<FrameworkElement?> Visual { get; }
    }

    private static readonly List<Entry> Entries = [];

    /// <summary>
    /// Adds (or replaces) a window's contribution. <paramref name="visual"/> is resolved at capture
    /// time, so a window may build or replace its root content after registering.
    /// </summary>
    internal static void Register(Window window, Func<FrameworkElement?> visual)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(visual);

        Unregister(window);
        Entries.Add(new Entry(new WeakReference<Window>(window), visual));
    }

    /// <summary>Drops a window's contribution (call from its <c>Closed</c> handler).</summary>
    internal static void Unregister(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Entries.RemoveAll(entry =>
            !entry.Window.TryGetTarget(out Window? registered) || ReferenceEquals(registered, window));
    }

    /// <summary>Live contributions, in registration order. Only called during a theme transition.</summary>
    internal static IReadOnlyList<(Window Window, FrameworkElement Visual)> CaptureTargets()
    {
        Entries.RemoveAll(entry => !entry.Window.TryGetTarget(out _));

        var targets = new List<(Window, FrameworkElement)>(Entries.Count);
        foreach (Entry entry in Entries)
        {
            if (entry.Window.TryGetTarget(out Window? window) && entry.Visual() is { } visual)
            {
                targets.Add((window, visual));
            }
        }

        return targets;
    }
}
