namespace WharfBar.Services;

/// <summary>
/// Process-wide fullscreen signal. Written by the AppBar-owning top bar; read by the dock.
/// </summary>
internal sealed class FullscreenState
{
    internal static FullscreenState Instance { get; } = new();

    private bool _isFullscreen;

    internal bool IsFullscreen => _isFullscreen;

    internal event EventHandler? Changed;

    internal void Set(bool isFullscreen)
    {
        if (_isFullscreen == isFullscreen)
        {
            return;
        }

        _isFullscreen = isFullscreen;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
