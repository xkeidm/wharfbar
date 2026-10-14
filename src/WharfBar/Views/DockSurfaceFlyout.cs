using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WharfBar.Native;
using WharfBar.Services;
using WharfBar.Views.Controls;

namespace WharfBar.Views;

/// <summary>
/// The dock's one flyout: window thumbnails on hover, or a folder stack on click.
/// Thumbnails unregister when the flyout closes. A cloaked or refused source keeps the title.
/// </summary>
internal sealed class DockSurfaceFlyout : IDisposable
{
    internal const int MaxThumbnails = 6;

    private readonly Popup _popup;
    private readonly Border _card;
    private readonly StackPanel _body;
    private readonly Action<IntPtr> _activate;
    private readonly List<IntPtr> _thumbnails = [];
    private readonly List<string> _folderStack = [];
    private readonly DispatcherTimer _closeTimer;
    private bool _disposed;
    private int _windowCount;

    /// <summary>Raised after the popup is shown. The dock hides its tooltip so the two labels do not stack.</summary>
    internal event Action<DockItemCell?>? Opened;

    /// <summary>Raised when the popup closes. The dock shows its tooltip again.</summary>
    internal event Action? Closed;

    internal DockSurfaceFlyout(Popup popup, Border card, StackPanel body, Action<IntPtr> activate)
    {
        _popup = popup;
        _card = card;
        _body = body;
        _activate = activate;
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _closeTimer.Tick += (_, _) => Close();
        _card.MouseEnter += (_, _) => CancelClose();
        _card.MouseLeave += (_, _) => ScheduleClose();
    }

    internal bool IsOpen => _popup.IsOpen;

    internal DockItemCell? Anchor { get; private set; }

    internal bool ShowingFolder { get; private set; }

    internal void CancelClose() => _closeTimer.Stop();

    internal void ScheduleClose()
    {
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    internal void ShowWindows(DockItemCell anchor, IReadOnlyList<AppWindowInfo> windows)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        CloseThumbnails();
        _body.Children.Clear();
        ShowingFolder = false;
        Anchor = anchor;

        if (windows.Count == 0)
        {
            Close();
            return;
        }

        // One window and no thumbnail: the tooltip already names the app. A title-only
        // card is a second bubble, shifted off the icon.
        if (windows.Count == 1 && !CanThumbnail(windows[0]))
        {
            Close();
            return;
        }

        _windowCount = windows.Count;
        bool caption = true;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        int shown = 0;
        foreach (AppWindowInfo window in windows)
        {
            if (shown >= MaxThumbnails)
            {
                break;
            }

            shown++;
            row.Children.Add(BuildWindowColumn(window, caption));
        }

        _body.Children.Add(row);
        Open(anchor);
    }

    internal void ShowFolder(DockItemCell anchor, string directory)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        CloseThumbnails();
        ShowingFolder = true;
        Anchor = anchor;
        _folderStack.Clear();
        _folderStack.Add(directory);
        if (!ShowFolderPage())
        {
            Close();
        }
    }

    internal void Close()
    {
        _closeTimer.Stop();
        CloseThumbnails();
        _body.Children.Clear();
        _folderStack.Clear();
        bool wasOpen = _popup.IsOpen || Anchor is not null;
        _popup.IsOpen = false;
        ShowingFolder = false;
        Anchor = null;
        _windowCount = 0;
        if (wasOpen)
        {
            Closed?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();
        _closeTimer.Stop();
    }

    private bool ShowFolderPage()
    {
        if (_folderStack.Count == 0)
        {
            return false;
        }

        string directory = _folderStack[^1];
        if (!DockFolderListing.TryRead(directory, out DockFolderPage page))
        {
            Debug.WriteLine($"WharfBar folder stack rejected: {directory}");
            return false;
        }

        _body.Children.Clear();
        var stack = new StackPanel();
        if (_folderStack.Count > 1)
        {
            stack.Children.Add(MakeRow("Geri", () =>
            {
                _folderStack.RemoveAt(_folderStack.Count - 1);
                _ = ShowFolderPage();
            }));
        }

        foreach (DockFolderEntry entry in page.Entries)
        {
            DockFolderEntry captured = entry;
            stack.Children.Add(MakeRow(
                captured.IsDirectory ? captured.Name + " \\" : captured.Name,
                () => OpenEntry(captured)));
        }

        if (page.Truncated)
        {
            stack.Children.Add(Note($"İlk {DockFolderListing.MaxVisibleRows} öğe"));
        }

        if (page.Entries.Count == 0)
        {
            stack.Children.Add(Note("Klasör boş"));
        }

        stack.Children.Add(MakeRow("Explorer'da aç", () => DockShellOpen.TryOpen(directory)));
        _body.Children.Add(stack);
        if (Anchor is not null)
        {
            Open(Anchor);
        }

        return true;
    }

    private void OpenEntry(DockFolderEntry entry)
    {
        if (!entry.IsDirectory)
        {
            _ = DockShellOpen.TryOpen(entry.FullPath);
            return;
        }

        _folderStack.Add(entry.FullPath);
        if (!ShowFolderPage())
        {
            _folderStack.RemoveAt(_folderStack.Count - 1);
            Debug.WriteLine($"WharfBar folder stack did not enter {entry.FullPath}");
        }
    }

    private static TextBlock Note(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(8, 6, 8, 2),
            Opacity = 0.7,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        return block;
    }

    private Button MakeRow(string text, Action click, bool center = false)
    {
        var button = new Button
        {
            Content = text,
            HorizontalContentAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            MaxWidth = 280,
        };
        button.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        button.Click += (_, _) => click();
        return button;
    }

    private static bool CanThumbnail(AppWindowInfo window) =>
        !window.IsMinimized && !DwmThumbnail.IsCloaked(window.Handle);

    private UIElement BuildWindowColumn(AppWindowInfo window, bool caption)
    {
        var column = new StackPanel { Width = 168, Margin = new Thickness(4, 0, 4, 0) };
        if (CanThumbnail(window))
        {
            var host = new DockThumbnailHost { Width = 160, Height = 90 };
            column.Children.Add(host);
            IntPtr source = window.Handle;
            host.Loaded += (_, _) => Register(host, source);
            host.SizeChanged += (_, _) => UpdateHost(host);
        }

        if (!caption && column.Children.Count > 0)
        {
            return column;
        }

        string title = string.IsNullOrWhiteSpace(window.Title) ? "Başlıksız pencere" : window.Title;
        column.Children.Add(MakeRow(title, () =>
        {
            _activate(window.Handle);
            Close();
        }, center: true));
        return column;
    }

    private void Register(DockThumbnailHost host, IntPtr source)
    {
        if (_disposed || host.HostHandle == IntPtr.Zero)
        {
            return;
        }

        if (!DwmThumbnail.TryRegister(host.HostHandle, source, out IntPtr thumb))
        {
            host.Visibility = Visibility.Collapsed;
            host.Height = 0;
            // A single window with no picture is only a second copy of the tooltip.
            if (_thumbnails.Count == 0 && _windowCount <= 1)
            {
                Close();
            }

            return;
        }

        _thumbnails.Add(thumb);
        host.Tag = thumb;
        UpdateHost(host);
    }

    private static void UpdateHost(DockThumbnailHost host)
    {
        if (host.Tag is not IntPtr thumb || thumb == IntPtr.Zero)
        {
            return;
        }

        int width = Math.Max(1, (int)Math.Round(host.ActualWidth));
        int height = Math.Max(1, (int)Math.Round(host.ActualHeight));
        if (host.ActualWidth <= 1 || host.ActualHeight <= 1)
        {
            GetClientSize(host.HostHandle, out width, out height);
        }

        DwmThumbnail.Update(thumb, width, height);
    }

    private void CloseThumbnails()
    {
        foreach (IntPtr thumb in _thumbnails)
        {
            DwmThumbnail.Unregister(thumb);
        }

        _thumbnails.Clear();
    }

    private void Open(DockItemCell anchor)
    {
        _popup.PlacementTarget = anchor;
        _popup.Placement = PlacementMode.Custom;
        _popup.CustomPopupPlacementCallback = (tipSize, targetSize, _) =>
        {
            double scale = VisualTreeHelper.GetDpi(anchor).DpiScaleX;
            (double x, double y) = DockTipPlacement.CenterAbove(
                tipSize.Width, tipSize.Height, targetSize.Width, scale);
            return
            [
                new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal),
            ];
        };
        _popup.IsOpen = true;
        CancelClose();
        Opened?.Invoke(anchor);
    }

    private static void GetClientSize(IntPtr hwnd, out int width, out int height)
    {
        width = 160;
        height = 90;
        if (hwnd != IntPtr.Zero && GetClientRect(hwnd, out RECT rect) && rect.Width > 0 && rect.Height > 0)
        {
            width = rect.Width;
            height = rect.Height;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
}
