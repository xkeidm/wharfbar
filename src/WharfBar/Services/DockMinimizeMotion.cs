namespace WharfBar.Services;

/// <summary>
/// Whether a minimize snapshot may play. Reduced motion
/// (<c>SystemParameters.ClientAreaAnimation</c> false) and a failed capture both skip it.
/// </summary>
internal static class DockMinimizeMotion
{
    internal const int DurationMs = 200;

    internal static bool ShouldPlay(bool clientAreaAnimation, bool captureSucceeded) =>
        clientAreaAnimation && captureSucceeded;
}
