namespace WharfBar.Services;

/// <summary>
/// One shell-icon cache for the whole process. Warm-up and the launchpad face must
/// use the same store or the icons decode twice.
/// </summary>
internal static class LaunchpadIcons
{
    internal static readonly AppIconService Shared = new();
}
