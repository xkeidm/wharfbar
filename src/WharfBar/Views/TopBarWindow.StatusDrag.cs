using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WharfBar.Models;

namespace WharfBar.Views;

public partial class TopBarWindow
{
    private string? _statusDragId;
    private Point _statusDragOrigin;
    private bool _statusDragActive;
    private bool _suppressStatusClick;
    private int _statusInsertAt = -1;
    private List<TopBarStatusItemIds.StatusStripSlot>? _statusDragSlots;
    private FrameworkElement? _statusDragElement;

    private void OnStatusStripDragDown(object sender, MouseButtonEventArgs e)
    {
        // A drag whose release never arrived must not swallow the next real click.
        _suppressStatusClick = false;
        _statusDragId = null;
        _statusDragElement = null;

        if (e.ChangedButton != MouseButton.Left || IsTrayMirrorIcon(e.OriginalSource as DependencyObject))
        {
            return;
        }

        FrameworkElement? hit = FindStatusElement(e.OriginalSource as DependencyObject);
        if (hit is null
            || hit.Visibility != Visibility.Visible
            || !TryGetIdForElement(hit, out string id))
        {
            return;
        }

        _statusDragId = id;
        _statusDragElement = hit;
        _statusDragOrigin = e.GetPosition(null);
    }

    private void OnStatusStripDragMove(object sender, MouseEventArgs e)
    {
        if (_statusDragId is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (!_statusDragActive)
            {
                _statusDragId = null;
                _statusDragElement = null;
            }

            return;
        }

        if (!_statusDragActive)
        {
            Point position = e.GetPosition(null);
            double dx = position.X - _statusDragOrigin.X;
            double dy = position.Y - _statusDragOrigin.Y;
            if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            BeginStatusDrag();
        }

        UpdateStatusDropCue(e.GetPosition(StatusStrip).X);
    }

    private void OnStatusStripDragUp(object sender, MouseButtonEventArgs e)
    {
        bool suppress = _suppressStatusClick || _statusDragActive;
        if (_statusDragActive)
        {
            CommitStatusDrag();
        }

        _suppressStatusClick = false;
        _statusDragId = null;
        _statusDragElement = null;
        if (suppress)
        {
            // Tunnel runs before the button's own PreviewMouseLeftButtonUp.
            e.Handled = true;
        }
    }

    private void OnStatusStripDragCancelRight(object sender, MouseButtonEventArgs e)
    {
        if (_statusDragActive)
        {
            EndStatusDragVisuals();
        }
    }

    private void OnStatusStripDragLostCapture(object sender, MouseEventArgs e)
    {
        if (!_statusDragActive)
        {
            return;
        }

        EndStatusDragVisuals();
    }

    private void BeginStatusDrag()
    {
        _statusDragSlots = CaptureStatusSlots();
        if (_statusDragId is null
            || _statusDragSlots.All(slot => !string.Equals(slot.Id, _statusDragId, StringComparison.Ordinal)))
        {
            _statusDragId = null;
            _statusDragElement = null;
            _statusDragSlots = null;
            return;
        }

        _statusDragActive = true;
        _suppressStatusClick = true;
        if (_statusDragElement is not null)
        {
            _statusDragElement.Opacity = 0.45;
        }

        if (_statusElements is not null)
        {
            foreach (FrameworkElement element in _statusElements.Values)
            {
                element.Cursor = Cursors.SizeAll;
            }
        }

        CloseFlyouts(animate: false);
        StatusStrip.CaptureMouse();
        UpdateStatusDropCue(StatusStripPointerX());
    }

    private double StatusStripPointerX()
    {
        try
        {
            return Mouse.GetPosition(StatusStrip).X;
        }
        catch (InvalidOperationException)
        {
            return _statusDragOrigin.X;
        }
    }

    private void UpdateStatusDropCue(double pointerX)
    {
        if (!_statusDragActive || _statusDragId is null || _statusDragSlots is null)
        {
            return;
        }

        int insertAt = TopBarStatusItemIds.ResolveInsertionIndex(_statusDragSlots, _statusDragId, pointerX);
        if (insertAt < 0)
        {
            HideStatusDropCue();
            return;
        }

        _statusInsertAt = insertAt;
        double edge = InsertionEdge(_statusDragSlots, _statusDragId, insertAt);
        Point onBar = StatusStrip.TranslatePoint(new Point(edge, 0), BarGrid);
        StatusDropCue.Margin = new Thickness(onBar.X - 1, 0, 0, 0);
        StatusDropCue.Visibility = Visibility.Visible;
    }

    private void CommitStatusDrag()
    {
        string? draggedId = _statusDragId;
        int insertAt = _statusInsertAt;
        List<TopBarStatusItemIds.StatusStripSlot>? slots = _statusDragSlots;
        EndStatusDragVisuals();

        if (draggedId is null || slots is null || insertAt < 0)
        {
            return;
        }

        var visible = new List<string>(slots.Count);
        foreach (TopBarStatusItemIds.StatusStripSlot slot in slots)
        {
            visible.Add(slot.Id);
        }

        List<string> items = TopBarStatusItemIds.Normalize(_config.Current.TopBar?.StatusItems);
        if (!TopBarStatusItemIds.MoveToInsertion(items, draggedId, visible, insertAt))
        {
            return;
        }

        PersistStatusItems(items);
    }

    private void EndStatusDragVisuals()
    {
        _statusDragActive = false;
        _statusDragId = null;
        _statusInsertAt = -1;
        _statusDragSlots = null;

        if (_statusDragElement is not null)
        {
            _statusDragElement.Opacity = 1;
            _statusDragElement = null;
        }

        if (_statusElements is not null)
        {
            foreach (FrameworkElement element in _statusElements.Values)
            {
                element.ClearValue(FrameworkElement.CursorProperty);
            }
        }

        HideStatusDropCue();
        if (StatusStrip.IsMouseCaptured)
        {
            StatusStrip.ReleaseMouseCapture();
        }
    }

    private void HideStatusDropCue() => StatusDropCue.Visibility = Visibility.Collapsed;

    private List<TopBarStatusItemIds.StatusStripSlot> CaptureStatusSlots()
    {
        var slots = new List<TopBarStatusItemIds.StatusStripSlot>();
        foreach (object child in StatusStrip.Children)
        {
            if (child is not FrameworkElement element
                || element.Visibility != Visibility.Visible
                || element.ActualWidth <= 0
                || !TryGetIdForElement(element, out string id))
            {
                continue;
            }

            Point left = element.TranslatePoint(new Point(0, 0), StatusStrip);
            slots.Add(new TopBarStatusItemIds.StatusStripSlot(id, left.X, element.ActualWidth));
        }

        return slots;
    }

    private static double InsertionEdge(
        IReadOnlyList<TopBarStatusItemIds.StatusStripSlot> slots,
        string draggedId,
        int insertAt)
    {
        int seen = 0;
        double lastRight = 0;
        foreach (TopBarStatusItemIds.StatusStripSlot slot in slots)
        {
            if (string.Equals(slot.Id, draggedId, StringComparison.Ordinal))
            {
                continue;
            }

            if (seen == insertAt)
            {
                return slot.Left;
            }

            lastRight = slot.Left + slot.Width;
            seen++;
        }

        return lastRight;
    }

    private static bool IsTrayMirrorIcon(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: TrayMirrorItem })
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
