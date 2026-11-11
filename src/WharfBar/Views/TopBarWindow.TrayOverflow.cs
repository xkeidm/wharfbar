using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class TopBarWindow
{
    private bool _trayOverflowClosePending;
    private bool _haveTraySnapshot;
    private bool _trayOverflowDragActive;
    private bool _trayOverflowUiStale;
    private IReadOnlyList<TrayMirrorItem> _trayMirrorSnapshot = [];
    private Point _trayPressOrigin;
    private Border? _trayPressHost;
    private bool _trayDragMoved;
    private bool _trayDragFromBox;
    private string? _trayDragKey;

    /// <summary>Last chevron tooltip text, so the 4 s poll does not churn the tooltip object.</summary>
    private string? _trayOverflowTooltip;

    private void AttachTrayOverflowBox()
    {
        TrayOverflowHost.DragStarted += OnTrayBoxDragStarted;
        TrayOverflowHost.DragHover += UpdateTrayPointerHover;
        TrayOverflowHost.DragCommit += (source, args) => FinishTrayPointerDrag(commit: true, source, args);
        TrayOverflowHost.DragLost += OnTrayBoxDragLost;
        TrayOverflowHost.IconInvoked += OnTrayOverflowIconInvoked;
        TrayOverflowHost.NativeOverflowRequested += OnTrayOverflowNativeRequested;
        TrayOverflowHost.KeyDown += OnFlyoutHostKeyDown;
    }

    private void OnTrayMirrorItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: TrayMirrorItem } host)
        {
            return;
        }

        e.Handled = true;
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _trayPressHost = host;
        _trayPressOrigin = e.GetPosition(null);
        _trayDragMoved = false;
        host.CaptureMouse();
        host.LostMouseCapture -= OnTrayMirrorItemLostCapture;
        host.LostMouseCapture += OnTrayMirrorItemLostCapture;
    }

    private void OnTrayMirrorItemMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border { Tag: TrayMirrorItem item } host
            || !ReferenceEquals(_trayPressHost, host)
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!_trayDragMoved)
        {
            Point now = e.GetPosition(null);
            if (!TrayOverflowMembership.ExceedsDragThreshold(
                    now.X - _trayPressOrigin.X,
                    now.Y - _trayPressOrigin.Y,
                    SystemParameters.MinimumHorizontalDragDistance,
                    SystemParameters.MinimumVerticalDragDistance))
            {
                return;
            }

            _trayDragMoved = true;
            BeginTrayPointerDrag(item.Key, fromBox: false);
        }

        UpdateTrayPointerHover(host, e);
    }

    private void OnTrayMirrorItemMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: TrayMirrorItem item } host || !ReferenceEquals(_trayPressHost, host))
        {
            return;
        }

        bool dragged = _trayDragMoved;
        _trayPressHost = null;
        _trayDragMoved = false;
        e.Handled = true;
        if (dragged)
        {
            FinishTrayPointerDrag(commit: true, host, e);
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
        ForwardTrayClick(item, kind, host);
    }

    private void OnTrayMirrorItemLostCapture(object sender, MouseEventArgs e)
    {
        if (!_trayDragMoved || _trayPressHost is null)
        {
            return;
        }

        Border host = _trayPressHost;
        _trayPressHost = null;
        _trayDragMoved = false;
        bool commit = sender is FrameworkElement source
            && (TryPointerOverBox(source, e) || (_trayDragFromBox && TryPointerOver(source, e, StatusStrip)));
        FinishTrayPointerDrag(commit, host, e);
    }

    private void OnTrayBoxDragLost(FrameworkElement source, MouseEventArgs e)
    {
        bool commit = TryPointerOverBox(source, e)
            || (_trayDragFromBox && TryPointerOver(source, e, StatusStrip));
        FinishTrayPointerDrag(commit, source, e);
    }

    private void OnTrayBoxDragStarted(string key) => BeginTrayPointerDrag(key, fromBox: true);

    private void BeginTrayPointerDrag(string key, bool fromBox)
    {
        _trayOverflowDragActive = true;
        _trayDragFromBox = fromBox;
        _trayDragKey = key;
    }

    private void UpdateTrayPointerHover(FrameworkElement source, MouseEventArgs e)
    {
        if (_trayDragKey is null)
        {
            return;
        }

        if (TryPointerOverBox(source, e))
        {
            // Drop-target plate through the live bar palette (the old fixed #33FFFFFF was a
            // dark-theme-only wash); ClearValue below restores the style's plain rest state.
            TrayOverflowButton.SetResourceReference(Border.BackgroundProperty, TopBarContrastPalette.HitFillKey);
            if (!_trayDragFromBox)
            {
                OpenTrayOverflowBox();
            }
        }
        else
        {
            TrayOverflowButton.ClearValue(Border.BackgroundProperty);
        }
    }

    private void FinishTrayPointerDrag(bool commit, FrameworkElement? source, MouseEventArgs? e)
    {
        string? key = _trayDragKey;
        bool fromBox = _trayDragFromBox;
        _trayDragKey = null;
        _trayDragFromBox = false;
        _trayOverflowDragActive = false;
        TrayOverflowButton.ClearValue(Border.BackgroundProperty);

        bool overBox = false;
        bool overStrip = false;
        if (commit && source is not null && e is not null)
        {
            overBox = TryPointerOverBox(source, e);
            overStrip = TryPointerOver(source, e, StatusStrip);
        }

        if (key is null)
        {
            FlushTrayOverflowUi();
            return;
        }

        if (overBox)
        {
            ParkTrayIcon(key);
        }
        else if (fromBox && overStrip)
        {
            RestoreTrayIcon(key);
        }
        else
        {
            FlushTrayOverflowUi();
        }
    }

    private bool TryPointerOverBox(FrameworkElement source, MouseEventArgs e) =>
        TryPointerOver(source, e, TrayOverflowButton)
        || (TrayOverflowPopup.IsOpen && TryPointerOver(source, e, TrayOverflowHost));

    private static bool TryPointerOver(FrameworkElement source, MouseEventArgs e, FrameworkElement target)
    {
        if (!target.IsVisible || target.ActualWidth <= 0 || target.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            Point screen = source.PointToScreen(e.GetPosition(source));
            Point local = target.PointFromScreen(screen);
            return local.X >= 0 && local.Y >= 0 && local.X <= target.ActualWidth && local.Y <= target.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void FlushTrayOverflowUi()
    {
        if (!_trayOverflowUiStale || !_haveTraySnapshot || _trayOverflowDragActive)
        {
            return;
        }

        _trayOverflowUiStale = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_haveTraySnapshot && !_trayOverflowDragActive)
            {
                ApplyTrayMirror(_trayMirrorSnapshot);
            }
        });
    }

    private void ToggleTrayOverflowBox()
    {
        if (TrayOverflowPopup.IsOpen)
        {
            CloseTrayOverflowBox();
            return;
        }

        OpenTrayOverflowBox();
    }

    private void OpenTrayOverflowBox()
    {
        if (!Monitor.IsPrimary || TrayOverflowPopup.IsOpen)
        {
            return;
        }

        CloseIsland();
        CloseQuickSettings();
        CloseBrightness();
        CloseMedia();
        CloseClockPanel();
        CancelPendingClose(TrayOverflowHost, ref _trayOverflowClosePending);
        if (_haveTraySnapshot)
        {
            TrayOverflowHost.SetItems(ParkedTrayIcons(
                _trayMirrorSnapshot,
                _config.Current.TopBar?.TrayOverflowIcons));
        }

        TrayOverflowPopup.PlacementTarget = TrayOverflowButton;
        TrayOverflowPopup.IsOpen = true;
    }

    private void OnTrayOverflowPopupOpened(object sender, EventArgs e)
    {
        CancelPendingClose(TrayOverflowHost, ref _trayOverflowClosePending);
        TrayOverflowHost.OnFlyoutOpened();
        AnimateFlyoutOpen(TrayOverflowHost);
    }

    private void CloseTrayOverflowBox(bool animate = true)
    {
        if (!TrayOverflowPopup.IsOpen)
        {
            _trayOverflowClosePending = false;
            return;
        }

        if (animate && !_trayOverflowClosePending)
        {
            _trayOverflowClosePending = true;
            AnimateFlyoutClose(TrayOverflowHost, () =>
            {
                _trayOverflowClosePending = false;
                TrayOverflowPopup.IsOpen = false;
            });
        }
        else
        {
            CancelPendingClose(TrayOverflowHost, ref _trayOverflowClosePending);
            TrayOverflowPopup.IsOpen = false;
        }
    }

    private void OnTrayOverflowIconInvoked(TrayOverflowInvokeRequest request) =>
        ForwardTrayClick(request.Item, request.Kind, request.Source);

    private void OnTrayOverflowNativeRequested(object? sender, EventArgs e)
    {
        CloseTrayOverflowBox(animate: false);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _engine.TrayOverflow.TryOpen()));
    }

    private void ParkTrayIcon(string key) => CommitTrayOverflow(TrayOverflowMembership.MoveIn(
        _config.Current.TopBar?.TrayOverflowIcons,
        key));

    private void RestoreTrayIcon(string key) => CommitTrayOverflow(TrayOverflowMembership.MoveOut(
        _config.Current.TopBar?.TrayOverflowIcons,
        key));

    private void CommitTrayOverflow(List<string> next)
    {
        TopBarConfig top = _config.Current.TopBar;
        IReadOnlyList<string> current = TrayOverflowMembership.Normalize(top.TrayOverflowIcons);
        if (current.SequenceEqual(next, StringComparer.Ordinal))
        {
            return;
        }

        top.TrayOverflowIcons = next;
        _config.Save(_config.Current);
        if (!_haveTraySnapshot)
        {
            return;
        }

        if (_trayOverflowDragActive)
        {
            _trayOverflowUiStale = true;
            return;
        }

        // After the mouse handler returns, so the dragged icon is not removed under the pointer.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_haveTraySnapshot && !_trayOverflowDragActive)
            {
                ApplyTrayMirror(_trayMirrorSnapshot);
            }
        });
    }

    private List<TrayMirrorItem> ParkedTrayIcons(
        IReadOnlyList<TrayMirrorItem> items,
        IEnumerable<string>? box)
    {
        var byKey = new Dictionary<string, TrayMirrorItem>(StringComparer.Ordinal);
        foreach (TrayMirrorItem item in items)
        {
            byKey[item.Key] = item;
        }

        var parked = new List<TrayMirrorItem>();
        foreach (string key in TrayOverflowMembership.BoxedKeys(byKey.Keys, box))
        {
            if (byKey.TryGetValue(key, out TrayMirrorItem? item))
            {
                parked.Add(item);
            }
        }

        // The mirror host collapses when nothing is left on the bar; the chevron then has to say
        // why instead of showing an unexplained gap (ADR-009 keeps hidden icons in Win+B).
        UpdateTrayOverflowTooltip(visibleIconCount: items.Count - parked.Count, mirrorItemCount: items.Count);
        return parked;
    }

    /// <summary>
    /// Keeps the chevron tooltip honest for an empty mirror: "still reading" vs "the tray really has
    /// no app icons — open Windows' hidden icons (Win+B)". Only a <b>verified</b> empty tray
    /// (<see cref="TrayMirrorState.TrayConfirmedEmpty"/>) claims the latter; an unread or
    /// inconclusive read never pretends to be a fact. No layout/style change, tooltip text only.
    /// </summary>
    private void UpdateTrayOverflowTooltip(int visibleIconCount, int mirrorItemCount)
    {
        TrayOverflowTooltip tooltip = TrayMirrorStates.TooltipFor(
            visibleIconCount,
            mirrorItemCount,
            _engine.TrayMirror.State);

        string text = tooltip switch
        {
            TrayOverflowTooltip.NoVisibleIcons => Strings.TrayOverflow_NoVisibleIcons,
            TrayOverflowTooltip.Unread => Strings.TrayOverflow_Unread,
            _ => Strings.TopBar_TrayOverflow,
        };

        if (string.Equals(_trayOverflowTooltip, text, StringComparison.Ordinal))
        {
            return;
        }

        _trayOverflowTooltip = text;
        SetPlainToolTip(TrayOverflowButton, text);
    }
}
