namespace WharfBar.Services;

/// <summary>
/// Auto-hide skeleton. <see cref="Enabled"/> comes from config; real proximity behavior comes later.
/// </summary>
internal sealed class DockAutoHideController
{
    internal DockAutoHideController(bool enabled = false)
    {
        Enabled = enabled;
    }

    internal bool Enabled { get; }

    internal void Show() { }

    internal void Hide() { }

    internal void OnPointerNear() { }
}
