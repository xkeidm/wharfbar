using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>Owns the single camera capture and tells the primary island when it changes.</summary>
internal static class CameraMirrorHost
{
    private static CameraPreview? _preview;

    internal static CameraPreview? Preview => _preview;

    internal static event EventHandler? Updated;

    internal static void Apply(bool enabled)
    {
        if (enabled)
        {
            _preview ??= new CameraPreview();
            _preview.Updated -= OnUpdated;
            _preview.Updated += OnUpdated;
            _ = _preview.StartAsync();
            return;
        }

        if (_preview is null)
        {
            return;
        }

        _preview.Updated -= OnUpdated;
        _preview.Dispose();
        _preview = null;
        Updated?.Invoke(null, EventArgs.Empty);
    }

    private static void OnUpdated(object? sender, EventArgs e) => Updated?.Invoke(sender, e);
}
