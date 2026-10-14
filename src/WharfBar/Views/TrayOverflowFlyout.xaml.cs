using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Horizontal icon capsule for parked mirror icons. Names stay on the tooltip.
/// Dragging an icon back onto the status strip restores it.
/// </summary>
public partial class TrayOverflowFlyout : UserControl
{
    private Point _pressOrigin;
    private Border? _pressHost;
    private bool _dragStarted;

    public TrayOverflowFlyout()
    {
        InitializeComponent();
    }

    internal event Action<string>? DragStarted;

    internal event Action<FrameworkElement, MouseEventArgs>? DragHover;

    internal event Action<FrameworkElement, MouseEventArgs>? DragCommit;

    internal event Action<FrameworkElement, MouseEventArgs>? DragLost;

    internal event Action<TrayOverflowInvokeRequest>? IconInvoked;

    internal event EventHandler? NativeOverflowRequested;

    internal void OnFlyoutOpened()
    {
        FlyoutRoot.Background = FlyoutSurface.Create();
    }

    internal void SetItems(IReadOnlyList<TrayMirrorItem> items)
    {
        bool empty = items.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Icons.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (SameIcons(items))
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (Icons.Children[i] is Border cell)
                {
                    RefreshCell(cell, items[i]);
                }
            }

            return;
        }

        Icons.Children.Clear();
        foreach (TrayMirrorItem item in items)
        {
            Icons.Children.Add(CreateCell(item));
        }
    }

    private bool SameIcons(IReadOnlyList<TrayMirrorItem> items)
    {
        if (Icons.Children.Count != items.Count)
        {
            return false;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (Icons.Children[i] is not Border { Tag: TrayMirrorItem existing }
                || !string.Equals(existing.Key, items[i].Key, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void RefreshCell(Border cell, TrayMirrorItem item)
    {
        cell.Tag = item;
        cell.ToolTip = LabelOf(item);
        if (cell.Child is Image image)
        {
            image.Source = item.Icon;
        }
    }

    private Border CreateCell(TrayMirrorItem item)
    {
        var image = new Image
        {
            Source = item.Icon,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var cell = new Border
        {
            Style = (Style)FindResource("TrayOverflowIcon"),
            Child = image,
            Tag = item,
            ToolTip = LabelOf(item),
        };
        cell.MouseLeftButtonDown += OnCellMouseLeftButtonDown;
        cell.MouseMove += OnCellMouseMove;
        cell.MouseLeftButtonUp += OnCellMouseLeftButtonUp;
        cell.LostMouseCapture += OnCellLostCapture;
        cell.PreviewMouseRightButtonUp += OnCellRightUp;
        cell.MouseRightButtonUp += OnCellRightUp;
        return cell;
    }

    private static string LabelOf(TrayMirrorItem item) =>
        string.IsNullOrWhiteSpace(item.Tooltip) ? item.Key : item.Tooltip;

    private void OnNativeOverflowClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        NativeOverflowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCellMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border host)
        {
            return;
        }

        e.Handled = true;
        _pressHost = host;
        _pressOrigin = e.GetPosition(null);
        _dragStarted = false;
        host.CaptureMouse();
    }

    private void OnCellMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border { Tag: TrayMirrorItem item } host
            || !ReferenceEquals(_pressHost, host)
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!_dragStarted)
        {
            Point now = e.GetPosition(null);
            if (!TrayOverflowMembership.ExceedsDragThreshold(
                    now.X - _pressOrigin.X,
                    now.Y - _pressOrigin.Y,
                    SystemParameters.MinimumHorizontalDragDistance,
                    SystemParameters.MinimumVerticalDragDistance))
            {
                return;
            }

            _dragStarted = true;
            DragStarted?.Invoke(item.Key);
        }

        DragHover?.Invoke(host, e);
    }

    private void OnCellMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: TrayMirrorItem item } host || !ReferenceEquals(_pressHost, host))
        {
            return;
        }

        bool dragged = _dragStarted;
        _pressHost = null;
        _dragStarted = false;
        e.Handled = true;
        if (dragged)
        {
            DragCommit?.Invoke(host, e);
            if (host.IsMouseCaptured)
            {
                host.ReleaseMouseCapture();
            }

            return;
        }

        if (host.IsMouseCaptured)
        {
            host.ReleaseMouseCapture();
        }

        TrayClick kind = e.ClickCount >= 2 ? TrayClick.DoubleLeft : TrayClick.Left;
        IconInvoked?.Invoke(new TrayOverflowInvokeRequest(item, kind, host));
    }

    private void OnCellLostCapture(object sender, MouseEventArgs e)
    {
        if (!_dragStarted || sender is not Border host)
        {
            return;
        }

        _dragStarted = false;
        _pressHost = null;
        DragLost?.Invoke(host, e);
    }

    private void OnCellRightUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: TrayMirrorItem item } host)
        {
            return;
        }

        bool forward = e.RoutedEvent == UIElement.PreviewMouseRightButtonUpEvent;
        e.Handled = true;
        if (!forward)
        {
            return;
        }

        if (Mouse.Captured != null)
        {
            Mouse.Capture(null);
        }

        IconInvoked?.Invoke(new TrayOverflowInvokeRequest(item, TrayClick.Right, host));
    }
}

internal sealed record TrayOverflowInvokeRequest(TrayMirrorItem Item, TrayClick Kind, FrameworkElement Source);
