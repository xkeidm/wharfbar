namespace WharfBar.Services;

/// <summary>
/// Auto-hide skeleton for Faz 4. Always disabled; real proximity behavior comes later.
/// </summary>
internal sealed class DockAutoHideController
{
    internal bool Enabled => false;

    internal void Show() { }

    internal void Hide() { }

    internal void OnPointerNear() { }
}
