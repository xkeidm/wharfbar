using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;

namespace WharfBar.Services;

/// <summary>
/// One shared camera preview for the island pill. A denied or busy camera
/// leaves <see cref="Frame"/> null and sets <see cref="Status"/>.
/// </summary>
internal sealed class CameraPreview : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private MediaCapture? _capture;
    private bool _busy;
    private bool _disposed;

    internal ImageSource? Frame { get; private set; }

    internal string Status { get; private set; } = "Kamera";

    internal bool IsRunning => _capture is not null;

    internal event EventHandler? Updated;

    internal async Task StartAsync()
    {
        if (_disposed || _capture is not null || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var capture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            };
            await capture.InitializeAsync(settings);
            _capture = capture;
            Status = "Kamera";
            Updated?.Invoke(this, EventArgs.Empty);
            _ = PullAsync();
        }
        catch (Exception)
        {
            Status = "Kamera yok";
            Frame = null;
            await StopAsync();
            Updated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _busy = false;
        }
    }

    internal async Task StopAsync()
    {
        MediaCapture? capture = _capture;
        _capture = null;
        Frame = null;
        if (capture is null)
        {
            return;
        }

        try
        {
            await capture.StopPreviewAsync();
        }
        catch (Exception)
        {
            // Preview may never have started.
        }

        capture.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = StopAsync();
    }

    private async Task PullAsync()
    {
        while (!_disposed && _capture is { } capture)
        {
            try
            {
                using VideoFrame frame = await capture.GetPreviewFrameAsync();
                SoftwareBitmap? bitmap = frame.SoftwareBitmap;
                if (bitmap is null)
                {
                    Status = "Kamera yok";
                    Frame = null;
                }
                else
                {
                    using SoftwareBitmap bgra = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    int width = bgra.PixelWidth;
                    int height = bgra.PixelHeight;
                    byte[] pixels = new byte[4 * width * height];
                    bgra.CopyToBuffer(pixels.AsBuffer());
                    _dispatcher.Invoke(() =>
                    {
                        var image = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        image.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);
                        image.Freeze();
                        Frame = image;
                        Status = "Kamera";
                    });
                }
            }
            catch (Exception)
            {
                Status = "Kamera yok";
                Frame = null;
            }

            Updated?.Invoke(this, EventArgs.Empty);
            await Task.Delay(400);
        }
    }
}
