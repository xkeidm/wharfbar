namespace WharfBar.Services;

/// <summary>
/// Fired after the top bar finishes an AppBar placement that may have resized the work area.
/// </summary>
internal static class WorkAreaChanged
{
    internal static event EventHandler? Raised;

    internal static void Notify() => Raised?.Invoke(null, EventArgs.Empty);
}
