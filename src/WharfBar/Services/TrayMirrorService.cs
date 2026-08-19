using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Polls the real tray toolbar (ADR-009) and publishes a filtered snapshot of third-party
/// icons for the top bar mirror. Read-only: never modifies shell state; nothing to restore.
/// </summary>
internal sealed class TrayMirrorService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly Dispatcher _dispatcher;
    private readonly System.Threading.Timer _timer;
    private ImageSource? _fallback;
    private bool _disposed;

    internal TrayMirrorService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timer = new System.Threading.Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    internal event EventHandler? Changed;

    internal IReadOnlyList<TrayMirrorItem> Items { get; private set; } = [];

    internal void Reenumerate() => _ = Task.Run(Poll);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            IntPtr toolbar = TrayToolbar.FindToolbarHwnd();
            IReadOnlyList<TrayButtonInfo> buttons = toolbar == IntPtr.Zero
                ? []
                : TrayToolbar.ReadButtons(toolbar);

            var items = new List<TrayMirrorItem>(buttons.Count);
            for (int i = 0; i < buttons.Count; i++)
            {
                TrayButtonInfo b = buttons[i];
                if (TraySystemIconFilter.IsSystemIcon(b.Tooltip)
                    || TraySystemIconFilter.IsWharfBarIcon(b.Tooltip))
                {
                    continue;
                }

                items.Add(BuildItem(toolbar, b, i));
            }

            Publish(items);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WharfBar: tray mirror poll failed — {ex.Message}");
        }
    }

    private TrayMirrorItem BuildItem(IntPtr toolbar, TrayButtonInfo button, int index)
    {
        ImageSource icon = Fallback;
        if (TrayToolbar.TryGetIcon(toolbar, button.ImageIndex, out IntPtr hIcon))
        {
            try
            {
                BitmapSource bmp = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                icon = bmp;
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        string key = $"{button.CommandId}\0{button.Tooltip}";
        return new TrayMirrorItem(key, button.CommandId, index, button.Tooltip, icon);
    }

    private ImageSource Fallback => _fallback ??= BuildFallback();

    private static ImageSource BuildFallback()
    {
        var bmp = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                null,
                new Point(10, 10),
                6,
                6);
        }

        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private void Publish(List<TrayMirrorItem> items)
    {
        if (_dispatcher.CheckAccess())
        {
            Items = items;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            Items = items;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}