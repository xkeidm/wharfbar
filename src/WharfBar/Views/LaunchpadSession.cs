using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// What one launchpad face is allowed to do. Faces draw UI only; the host opens apps,
/// persists the style, and closes the window.
/// </summary>
internal sealed class LaunchpadSession
{
    internal required AppConfig Config { get; init; }

    internal required IReadOnlyList<LaunchpadApp> Apps { get; set; }

    internal required Func<LaunchpadApp, ImageSource> Icon { get; init; }

    internal required Action<LaunchpadApp> Open { get; init; }

    internal required Action Close { get; init; }

    internal required Action<string> UseStyle { get; init; }

    internal required Action OpenWindowsSettings { get; init; }

    internal required Action OpenWharfSettings { get; init; }

    internal required Action OpenWidgets { get; init; }

    internal required Action OpenPlugins { get; init; }

    internal required Action<string> OpenPath { get; init; }

    internal IReadOnlyList<LaunchpadApp> Pinned { get; init; } = [];

    internal IReadOnlyList<LaunchpadRecentItem> Recent { get; set; } = [];

    internal LaunchpadPhone? Phone { get; init; }

    /// <summary>Phone Link card. Null hides the block. Missing battery or notices stay hidden.</summary>
    internal LaunchpadPhoneSnapshot? LinkedPhone { get; init; }

    /// <summary>True while the Start Menu scan has not finished. The face hides the empty-state line.</summary>
    internal bool CatalogPending { get; set; }

    /// <summary>Redraws the face after <see cref="Apps"/> or <see cref="Recent"/> change.</summary>
    internal Action? Reload { get; set; }

    internal string Style => LaunchpadStyles.Normalize(Config.Launchpad.Style);
}
