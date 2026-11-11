using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Native;
using WharfBar.Resources;
using WharfBar.Services;
using WharfBar.Views.Controls;

namespace WharfBar.Views;

/// <summary>
/// The dock's one flyout: a card per window of a running app (hover), or a folder stack
/// (click). A card is the preview, the window title, and the click target: the preview is a
/// static <c>PrintWindow</c> frame, because a DWM live thumbnail rejects both this layered
/// popup and a child HWND as its destination. Minimized windows use the frame WharfBar saw when
/// they minimized, then fall back to the app icon. Releases every capture when it closes.
/// </summary>
internal sealed class DockSurfaceFlyout : IDisposable
{
    private const int ColumnsPerRow = 4;
    private const double CardWidthDip = 184d;
    private const double CardOuterWidthDip = 192d;
    private const double PreviewWidthDip = 176d;
    private const double PreviewHeightDip = 99d;

    private readonly Popup _popup;
    private readonly Border _card;
    private readonly StackPanel _body;
    private readonly Action<IntPtr> _activate;
    private readonly List<PreviewSlot> _previews = [];
    private readonly List<string> _folderStack = [];
    private readonly DispatcherTimer _closeTimer;
    private CancellationTokenSource? _capture;
    private int _captureGeneration;
    private bool _disposed;

    /// <summary>Raised when the popup closes.</summary>
    internal event Action? Closed;

    internal DockSurfaceFlyout(Popup popup, Border card, StackPanel body, Action<IntPtr> activate)
    {
        _popup = popup;
        _card = card;
        _body = body;
        _activate = activate;
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
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

    internal void ShowWindows(
        DockItemCell anchor,
        IReadOnlyList<AppWindowInfo> windows,
        ImageSource? appIcon,
        Func<IntPtr, BitmapSource?>? cachedFrame = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(windows);
        ClosePreviews();
        _body.Children.Clear();
        ShowingFolder = false;
        Anchor = anchor;

        if (windows.Count == 0)
        {
            Close();
            return;
        }

        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            MaxWidth = ColumnsPerRow * CardOuterWidthDip,
        };
        foreach (AppWindowInfo window in windows)
        {
            wrap.Children.Add(BuildWindowCard(window, appIcon, cachedFrame));
        }

        _body.Children.Add(wrap);
        Open(anchor);
        StartCapture(windows, anchor);
    }

    internal void ShowFolder(DockItemCell anchor, string directory)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ClosePreviews();
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
        ClosePreviews();
        _body.Children.Clear();
        _folderStack.Clear();
        bool wasOpen = _popup.IsOpen || Anchor is not null;
        _popup.IsOpen = false;
        ShowingFolder = false;
        Anchor = null;
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
            stack.Children.Add(MakeRow(Strings.Qs_Back, () =>
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
            stack.Children.Add(Note($"First {DockFolderListing.MaxVisibleRows} items"));
        }

        if (page.Entries.Count == 0)
        {
            stack.Children.Add(Note("Folder is empty"));
        }

        stack.Children.Add(MakeRow("Open in Explorer", () => DockShellOpen.TryOpen(directory)));
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

    /// <summary>
    /// One window: a preview frame with the title under it. The whole card is the click target
    /// (a real button around a preview would fight the image for the hit test).
    /// </summary>
    private Border BuildWindowCard(
        AppWindowInfo window,
        ImageSource? appIcon,
        Func<IntPtr, BitmapSource?>? cachedFrame)
    {
        var image = new Image
        {
            Width = PreviewWidthDip,
            Height = PreviewHeightDip,
            Stretch = Stretch.Uniform,
            Source = appIcon,
            Opacity = appIcon is null ? 0d : 0.45d,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var preview = new Border
        {
            Width = PreviewWidthDip,
            Height = PreviewHeightDip,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = image,
        };
        // Theme brush, not a white wash: the card is repainted after every light/dark or accent
        // change and a hardcoded #1AFFFFFF is invisible on the light card.
        preview.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");

        string title = string.IsNullOrWhiteSpace(window.Title) ? Strings.Dock_UntitledWindow : window.Title;
        var caption = new TextBlock
        {
            Text = title,
            FontSize = 12,
            MaxWidth = PreviewWidthDip,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var stack = new StackPanel { Width = PreviewWidthDip };
        stack.Children.Add(preview);
        stack.Children.Add(caption);

        var card = new Border
        {
            Width = CardWidthDip,
            Padding = new Thickness(4),
            Margin = new Thickness(4, 0, 4, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = stack,
            ToolTip = title,
        };
        AutomationProperties.SetName(card, title);

        // The caption is trimmed; the balloon only carries the full title after a longer rest,
        // so moving across the cards does not pop text over the previews.
        ToolTipService.SetInitialShowDelay(card, 900);

        IntPtr handle = window.Handle;
        card.MouseEnter += (_, _) =>
        {
            CancelClose();
            card.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        };
        card.MouseLeave += (_, _) =>
        {
            card.ClearValue(Border.BackgroundProperty);
            card.Background = Brushes.Transparent;
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _activate(handle);
            Close();
        };

        // A minimized window's PrintWindow is the iconic stub, so a frame WharfBar captured
        // when the window minimized is the only real picture available for it.
        if (window.IsMinimized && cachedFrame?.Invoke(window.Handle) is { } shot)
        {
            image.Source = shot;
            image.Opacity = 1d;
        }

        _previews.Add(new PreviewSlot(window.Handle, image));
        return card;
    }

    /// <summary>
    /// Captures the live frames off the UI thread, one by one, and drops them into the cards.
    /// A stale capture (the flyout closed or moved to another app) is ignored by generation.
    /// </summary>
    private void StartCapture(IReadOnlyList<AppWindowInfo> windows, DockItemCell anchor)
    {
        // Cancel-only, never Dispose: the previous worker may still be inside the last
        // PrintWindow and reads its token afterwards.
        _capture?.Cancel();
        _capture = null;

        var targets = new List<AppWindowInfo>(windows.Count);
        foreach (AppWindowInfo window in windows)
        {
            if (!window.IsMinimized)
            {
                targets.Add(window);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        double scale = VisualTreeHelper.GetDpi(anchor).DpiScaleX;
        int maxWidthPx = Math.Max(1, (int)Math.Round(PreviewWidthDip * scale));
        int maxHeightPx = Math.Max(1, (int)Math.Round(PreviewHeightDip * scale));

        var capture = new CancellationTokenSource();
        _capture = capture;
        int generation = ++_captureGeneration;
        Dispatcher dispatcher = _card.Dispatcher;

        _ = Task.Run(() =>
        {
            try
            {
                foreach (AppWindowInfo window in targets)
                {
                    if (capture.IsCancellationRequested)
                    {
                        return;
                    }

                    BitmapSource? frame = DockWindowPreview.TryCapture(window.Handle);
                    if (frame is null)
                    {
                        continue;
                    }

                    IntPtr handle = window.Handle;
                    _ = dispatcher.BeginInvoke(() =>
                    {
                        if (_disposed
                            || capture.IsCancellationRequested
                            || generation != _captureGeneration
                            || FindPreview(handle) is not { } slot)
                        {
                            return;
                        }

                        slot.Image.Source = DockWindowPreview.ScaleToFit(frame, maxWidthPx, maxHeightPx);
                        slot.Image.Opacity = 1d;
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar dock preview capture failed: {ex.Message}");
            }
        });
    }

    private PreviewSlot? FindPreview(IntPtr handle)
    {
        foreach (PreviewSlot slot in _previews)
        {
            if (slot.Handle == handle)
            {
                return slot;
            }
        }

        return null;
    }

    private void ClosePreviews()
    {
        _captureGeneration++;
        _capture?.Cancel();
        _capture = null;
        _previews.Clear();
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
    }

    private sealed record PreviewSlot(IntPtr Handle, Image Image);
}
