using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WharfBar.Resources;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class TopBarWindow
{
    private const double IslandIdleDip = 108;
    private const double IslandHoverDip = 4;
    private const double IslandPadDip = 32;
    private const double IslandGlyphDip = 16;
    private const double IslandMeterDip = 72;
    // Centered slot outside the title: pill margin 24+24, padding 16+16, glyph, title gap.
    private const double IslandTitleChromeDip = 104;

    /// <summary>Grid margin on each side. Counted so the title ellipsizes before the chip meets a cluster.</summary>
    private const double IslandContentInsetDip = 10;
    private const double IslandSatelliteDip = 24;
    private const string GlyphClipboard = "\uE8C8";
    private const string GlyphMusic = "\uE90C";
    private const string GlyphBrightness = "\uE706";
    private const string GlyphDrop = "\uE8A5";
    private const string GlyphDragTarget = "\uE73E";
    private const string GlyphBluetooth = "\uE702";
    private const string GlyphCamera = "\uE722";

    /// <summary>Drop badge shown while a file drag hovers the pill.</summary>
    private static readonly string IslandDragBadgeText = Strings.Island_DragBadge;

    /// <summary>Joined edge while the pill is a drag target; popup chrome owns it when open.</summary>
    private static readonly Thickness IslandDragBorderThickness = new(1, 1, 1, 0);
    private const double IslandDragGlowOpacity = 0.35;

    /// <summary>Swallow micro-motion on a successful drop: compress, release past 1, settle.</summary>
    private const double IslandSwallowCompressMs = 80;
    private const double IslandSwallowReleaseMs = 110;
    private const double IslandSwallowSettleMs = 90;
    private const double IslandSwallowCompressScale = 0.96;
    private const double IslandSwallowReleaseScale = 1.05;

    private bool _islandClosePending;
    // The grow-from-the-pill open morph is running. WPF raises Popup.Opened after the popup's
    // first layout pass, and that pass can already trigger the reveal (popup width known ->
    // TryRevealIsland -> AnimateIsland). OnIslandPopupOpened must not cancel that morph as if
    // it were a stale close: doing so snapped the panel to its final scale/opacity, so the
    // documented ~200 ms grow opened as an instant pop on the first paint.
    private bool _islandOpeningMotion;
    // Prompter playback pin: while true the panel cannot auto-close; the pin carries the event (Task 3).
    private bool _islandPrompterPinned;
    // A close coming from a pill/satellite/keyboard click overrides the pin one time (it comes through the CloseIsland path).
    private bool _islandPrompterCloseOnce;
    private bool _islandAwaitingReveal;
    private bool _islandMeasuring;
    private bool _islandRevealPosted;
    private IslandPresence? _presence;
    private WallpaperService? _wallpaper;
    private IReadOnlyList<BluetoothDeviceInfo>? _btPrevious;
    private bool _btSeeded;
    private DispatcherTimer? _islandClock;
    private DispatcherTimer? _islandHoverWatch;
    private bool _islandFocusMusic;
    private IslandPanelBody _shownPanel;
    private bool _shownBack;
    private bool _islandPulseRunning;
    private bool _islandPressed;
    // Drag chrome is transient UI (not an IslandPillFace): one transition-gated boolean
    // freezes the face pipeline and feeds a drag width term into AnimatePillWidth.
    private bool _islandDragActive;
    private bool _islandDragValid;
    // The drag preview opens a beat after the pill's own drag chrome has landed, so the pill's
    // grow/glow is seen first; the close grace absorbs the popup's 2 DIP overlap with the pill.
    private DispatcherTimer? _islandDragPreviewTimer;
    private DispatcherTimer? _islandDragLeaveTimer;
    // The drag point is over the open preview panel (OLE cross-window hand-off): a pill DragLeave
    // that arrives after this is not a real leave and must not arm the close grace.
    private bool _islandDragOverPanel;
    // The popup is open and the pill's bottom edge is merged into the panel: the pill stroke
    // follows the panel rim so the unified outline stays continuous.
    private bool _islandJoined;
    // Closed-pill tint cache: the last album tint / hover state / bar-palette revision painted on
    // the capsule. Keeps the per-tick media snapshot pushes and pointer samples from rebuilding a
    // frozen brush, while a wallpaper-driven palette change still repaints it.
    private ClosedPillTintState _closedPillTintState = ClosedPillTintState.Invalid;
    private DropShadowEffect? _islandDragGlow;
    private string? _islandIdleCaptionText;
    private IslandPillFace? _shownFace;
    private string? _islandTip;
    private string _faceTitle = string.Empty;
    private double _faceBar = double.NaN;
    private double _faceLeft = double.NaN;
    private double _faceRight = double.NaN;
    private double _titleWidth;
    private double _meterFill;
    private double _pillWidth = double.NaN;
    private double _faceSatellites;
    // Screen pixel X of the pill center written by the last popup alignment. A pill width
    // change keeps the centered pill's screen center, so the panel does not need a new offset.
    private double? _alignedPillCenterPx;
    // Pill width (DIP) the popup's silhouette join was last built for. A centered width change
    // keeps the panel where it is but moves the pill's left/right edges — the fillet notch has to
    // be rebuilt or the joined pill visibly outgrows the panel's top edge.
    private double _joinPillWidthDip;
    // Last geometry-confirmed over-island sample; a change repaints the hover width even when
    // the presence view itself did not change (pinned panel, genuine leave).
    private bool _pointerOverIslandSample;
    private bool _captureExcluded;
    private IslandShotWindow? _shot;
    private Panel? _pillHome;
    private int _pillHomeIndex;
    private FrameworkElement? _pillSlot;
    private bool _pillParked;
    private Thickness _pillMargin;
    private HorizontalAlignment _pillAlignH;
    private VerticalAlignment _pillAlignV;
    private bool _idleLabelSet;
    private int _appliedLowBattery = int.MinValue;
    private IslandSatellite _selectedSatellite = IslandSatellite.None;
    private IslandIdleLabel _appliedIdleLabel;
    private bool _idlePreviewSet;
    private string? _appliedIdlePreview;

    private static long IslandNow() => Environment.TickCount64;

    private void OnIslandPillEnter(object sender, MouseEventArgs e) => SyncIslandPointer(IslandNow());

    private void OnIslandPillLeave(object sender, MouseEventArgs e) => SyncIslandPointer(IslandNow());

    /// <summary>
    /// Geometry-confirmed pointer sample. <c>MouseLeave</c> is not evidence of a real leave:
    /// the island popup is a separate window that overlaps the pill's lower edge and the
    /// shadow pad around the panel, so it can take the pointer (and raise a fake leave) while
    /// the cursor never left the island. <see cref="Mouse.GetPosition"/> asks the input device
    /// for the cursor in an element's coordinates, so both rects can be tested even when the
    /// popup owns the pointer.
    /// </summary>
    private void SyncIslandPointer(long now)
    {
        if (_presence is null)
        {
            return;
        }

        (bool overPill, bool overPanel) = SampleIslandPointer();
        bool over = overPill || overPanel;
        bool hoverChanged = over != _pointerOverIslandSample;
        _pointerOverIslandSample = over;
        IslandPresenceView before = _presence.View;
        long? deadline = _presence.NextDeadlineMs;
        _presence.SamplePointer(overPill, overPanel, now);
        // A pinned panel keeps the presence view unchanged on a leave, but the pill's hover
        // allowance still has to follow the pointer, so the geometry change alone repaints.
        if (hoverChanged || before != _presence.View || deadline != _presence.NextDeadlineMs)
        {
            ApplyIslandChrome(focusMusic: false);
        }
    }

    /// <summary>True while the cursor rests on the pill or on the open panel's surface/pad.</summary>
    private bool IsPointerOverIsland()
    {
        (bool overPill, bool overPanel) = SampleIslandPointer();
        bool over = overPill || overPanel;
        _pointerOverIslandSample = over;
        return over;
    }

    private (bool OverPill, bool OverPanel) SampleIslandPointer()
    {
        try
        {
            // Mouse.GetPosition is the last position WPF received from an input message of this
            // process. Once the pointer rests over another application (or the last WPF window
            // under it, the popup, was closed), it goes stale at the popup's click point and the
            // geometric sample keeps saying "over the pill": the hover panel then neither closes
            // on leave nor re-opens after a click-close, because the pointer never counts as
            // having re-entered. The real cursor position cannot go stale.
            if (!TryGetCursorInElement(IslandPill, out Point pill))
            {
                return (false, false);
            }

            Size pillSize = new(IslandPill.ActualWidth, IslandPill.ActualHeight);
            if (!IslandPopup.IsOpen)
            {
                return (IslandHoverRegion.Contains(pill, pillSize.Width, pillSize.Height), false);
            }

            if (!TryGetCursorInElement(IslandHost, out Point panel))
            {
                return (false, false);
            }

            Size panelSize = new(IslandHost.ActualWidth, IslandHost.ActualHeight);
            if (IslandHoverRegion.IsOverIsland(pill, pillSize, panel, panelSize))
            {
                // The pill and the panel overlap by design; the pill flag wins there.
                bool overPill = IslandHoverRegion.Contains(pill, pillSize.Width, pillSize.Height);
                return (overPill, !overPill);
            }
        }
        catch (InvalidOperationException)
        {
            // The element is not connected to a presentation source (window closing).
        }

        return (false, false);
    }

    /// <summary>Cursor position in an element's coordinates, from the real cursor (not WPF's cached input state).</summary>
    private static bool TryGetCursorInElement(Visual element, out Point point)
    {
        point = default;
        if (!NativeMethods.GetCursorPos(out POINT cursor))
        {
            return false;
        }

        try
        {
            point = element.PointFromScreen(new Point(cursor.X, cursor.Y));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void OnIslandPillDragEnter(object sender, DragEventArgs e)
    {
        // Only a primary-monitor file drag earns the target chrome; a text/URL drag leaves
        // the pill untouched. The state machine reads this gate, so there is no second source.
        _islandDragValid = Monitor.IsPrimary && e.Data.GetDataPresent(DataFormats.FileDrop);
        _islandDragOverPanel = false;
        CancelIslandDragLeave();
        if (_islandDragValid)
        {
            // Mark the drag now (suppresses the hover close and a pending hover open) but let the
            // preview delay open the panel, so the pill's own drag chrome animation is seen first.
            _presence?.BeginFileDrag(IslandNow(), openPanel: false);
        }

        SetIslandDragActive(true);
        if (_islandDragActive)
        {
            ArmIslandDragPreview();
        }
    }

    private void OnIslandPillDragLeave(object sender, DragEventArgs e)
    {
        // The open preview overlaps the pill's lower edge by ~2 DIP, so a leave can fire while
        // the drag is still over the island or while it moves down onto the preview. Close
        // through a short grace: a genuine re-enter (back on the pill) cancels it, and moving
        // onto the preview ends the preview predictably instead of leaving it undecidable.
        CancelIslandDragPreview();
        if (_islandDragOverPanel)
        {
            // The cross-window hand-off already happened; collapse the pill chrome without
            // scheduling the preview close.
            SetIslandDragActive(false);
            return;
        }

        if (IslandPopup.IsOpen)
        {
            ArmIslandDragLeave();
            return;
        }

        EndIslandDrag(keepPanel: false);
    }

    /// <summary>
    /// The pill is the primary drop target, but while the preview panel is open it covers most of
    /// the island: the panel takes the same file drop so a drag that follows the surface down into
    /// the opened shelf still lands there. The popup HWND already carries WPF's OLE drop
    /// registration (a live Explorer drag over the panel is still unmeasured), so this is an
    /// extension to the pill target; the pill's leave grace still closes the preview when the drag
    /// never reaches the panel.
    /// </summary>
    private void OnIslandPanelDragEnter(object sender, DragEventArgs e)
    {
        if (!_islandDragValid || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        _islandDragOverPanel = true;
        CancelIslandDragLeave();
        CancelIslandDragPreview();
        // The pointer has left the pill for the panel: the pill's chrome belongs to the pill.
        // The preview panel stays (the presence file-drag session owns it until the drop/leave).
        SetIslandDragActive(false);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnIslandPanelDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _islandDragValid && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnIslandPanelDragLeave(object sender, DragEventArgs e)
    {
        _islandDragOverPanel = false;
        if (!_islandDragValid)
        {
            return;
        }

        // Moving back up to the pill cancels this through the pill's DragEnter.
        CancelIslandDragPreview();
        ArmIslandDragLeave();
    }

    private void OnIslandPanelDrop(object sender, DragEventArgs e) => DropOnIsland(e);

    /// <summary>
    /// Opens the shelf under the pill while a file drag hovers it, so the user sees where the
    /// file will land before releasing. The pill's drag chrome is already showing; the delay
    /// keeps the panel from covering that first animation, and a fast drag that leaves before
    /// the delay opens no panel at all.
    /// </summary>
    private void ArmIslandDragPreview()
    {
        if (!_islandDragActive
            || _presence is null
            || IslandPopup.IsOpen
            || Island?.Config is not { Enabled: true, ShelfTab: true })
        {
            return;
        }

        DispatcherTimer timer = EnsureIslandDragPreviewTimer();
        timer.Stop();
        // Zero (animations off) still goes through one dispatcher turn: opening the popup inside
        // the OLE DragEnter callback re-enters the drag loop under the cursor and maximizes the
        // leave/enter churn at the popup's overlapping edge.
        double ms = IslandMotion.DragPreviewOpenMs(SystemParameters.ClientAreaAnimation);
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
        timer.Start();
    }

    private void OnIslandDragPreviewTick(object? sender, EventArgs e)
    {
        _islandDragPreviewTimer?.Stop();
        // A panel may have opened while the delay ran (transient music, a race with hover open):
        // then the shelf preview is no longer the surface under the pointer and is not forced in.
        if (_islandDragActive && !IslandPopup.IsOpen)
        {
            OpenIslandDragPreview();
        }
    }

    private void OpenIslandDragPreview()
    {
        if (!_islandDragActive
            || IslandPopup.IsOpen
            || _presence is null
            || Island?.Config is not { Enabled: true, ShelfTab: true })
        {
            return;
        }

        // The panel opens through the normal presence/chrome path, so the pill joins the panel
        // and the existing grow-from-the-pill open animation runs unchanged.
        _presence.BeginFileDrag(IslandNow());
        ApplyIslandChrome(focusMusic: false);
        if (IslandPopup.IsOpen)
        {
            // Light the shelf only on a panel that actually opened; otherwise the endless pulse
            // clock would outlive a popup that never appeared.
            IslandHost.ShowShelfDropTarget(true);
        }
    }

    private void CancelIslandDragPreview() => _islandDragPreviewTimer?.Stop();

    private DispatcherTimer EnsureIslandDragPreviewTimer()
    {
        if (_islandDragPreviewTimer is null)
        {
            _islandDragPreviewTimer = new DispatcherTimer();
            _islandDragPreviewTimer.Tick += OnIslandDragPreviewTick;
        }

        return _islandDragPreviewTimer;
    }

    private void ArmIslandDragLeave()
    {
        DispatcherTimer timer = EnsureIslandDragLeaveTimer();
        timer.Stop();
        // A hand-off debounce, not an animation: it stays on with animations off (the pill and the
        // panel race each other's DragLeave/DragEnter across the popup's overlapping top edge).
        timer.Interval = TimeSpan.FromMilliseconds(IslandMotion.DragLeaveGraceDelayMs);
        timer.Start();
    }

    private void OnIslandDragLeaveTick(object? sender, EventArgs e)
    {
        _islandDragLeaveTimer?.Stop();
        // The chrome may already be collapsed (the drag moved onto the preview panel); the
        // session flag is what is still open, so the preview closes on it.
        if (_islandDragValid)
        {
            EndIslandDrag(keepPanel: false);
        }
    }

    private void CancelIslandDragLeave() => _islandDragLeaveTimer?.Stop();

    private DispatcherTimer EnsureIslandDragLeaveTimer()
    {
        if (_islandDragLeaveTimer is null)
        {
            _islandDragLeaveTimer = new DispatcherTimer();
            _islandDragLeaveTimer.Tick += OnIslandDragLeaveTick;
        }

        return _islandDragLeaveTimer;
    }

    /// <summary>
    /// Ends the file-drag session: the drag preview is released first (a landed drop keeps the
    /// panel so the new shelf entry is visible; a cancel closes it), then the pill's drag chrome
    /// collapses and the face pipeline repaints from the same presence snapshot.
    /// </summary>
    private void EndIslandDrag(bool keepPanel)
    {
        CancelIslandDragPreview();
        CancelIslandDragLeave();
        IslandHost.ShowShelfDropTarget(false);
        _presence?.EndFileDrag(keepPanel, IslandNow());
        _islandDragValid = false;
        _islandDragOverPanel = false;
        if (_islandDragActive)
        {
            // SetIslandDragActive(false) runs UpdateIslandPill -> ApplyIslandChrome, which
            // observes the panel transition above (open kept / animated close).
            SetIslandDragActive(false);
            return;
        }

        ApplyIslandChrome(focusMusic: false);
    }
    /// <summary>
    /// The drag-target chrome is a transition-gated state, not an extra animation: enter
    /// swaps the pill chrome and widens it through <see cref="AnimatePillWidth"/>, exit
    /// rebuilds the frozen face and lets that same pipeline close the width.
    /// </summary>
    private void SetIslandDragActive(bool active)
    {
        // The chrome only engages for a primary-monitor file drag; the enter handler computes
        // that predicate once into _islandDragValid and every other caller passes false.
        bool chrome = active && _islandDragValid;
        // DragOver fires ~60 Hz; only a real state change may touch chrome.
        if (_islandDragActive == chrome)
        {
            return;
        }

        _islandDragActive = chrome;

        if (!chrome)
        {
            // The drag accent stroke is gone: a joined pill takes the panel rim back, a closed
            // one returns to the capsule style stroke.
            if (_islandJoined && IslandPopup.IsOpen)
            {
                ApplyIslandJoinRim();
            }
            else
            {
                IslandPill.ClearValue(Border.BorderBrushProperty);
            }

            if (!IslandPopup.IsOpen)
            {
                // The style's 1 DIP capsule is the closed default. An open popup owns the
                // joined edge (SetIslandJoined set it) until its own close restores it.
                IslandPill.ClearValue(Border.BorderThicknessProperty);
            }

            SetIslandDragGlow(0);
            if (IslandIdleCaption.Text == IslandDragBadgeText)
            {
                IslandIdleCaption.Text = _islandIdleCaptionText ?? Strings.Island_Clipboard;
            }

            // Face chrome was frozen during the drag; rebuild it and let AnimatePillWidth
            // close the width with its existing slow-close ease. ApplyIslandFace rewrites the
            // automation name unconditionally; dropping the tooltip cache forces its value too.
            _islandTip = null;
            UpdateIslandPill();
            return;
        }

        _islandIdleCaptionText ??= IslandIdleCaption.Text;
        IslandGlyph.Text = GlyphDragTarget;
        IslandGlyph.Visibility = Visibility.Visible;
        IslandIdleCaption.Text = IslandDragBadgeText;
        IslandIdleCaption.Visibility = Visibility.Visible;
        IslandTitle.Visibility = Visibility.Collapsed;
        IslandMeter.Visibility = Visibility.Collapsed;
        IslandCoverThumb.Visibility = Visibility.Collapsed;
        IslandCamera.Visibility = Visibility.Collapsed;
        CollapseIslandSatellite(IslandSatelliteFirst);
        CollapseIslandSatellite(IslandSatelliteSecond);
        IslandPill.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        if (IslandPill.BorderThickness != IslandDragBorderThickness)
        {
            IslandPill.BorderThickness = IslandDragBorderThickness;
        }

        SetIslandDragGlow(IslandDragGlowOpacity);
        // Screen readers and the tooltip must follow the badge, not the frozen face text;
        // the exit pipeline restores the face-driven values.
        AutomationProperties.SetName(IslandPill, IslandDragBadgeText);
        SetPlainToolTip(IslandPill, IslandDragBadgeText);
        // The drag target is a transient chrome state: drop the album tint while it is shown.
        UpdateClosedIslandPillFill();
        double content = IslandPadDip + IslandGlyphDip + 8 + MeasureIslandDragBadge() + IslandHoverDip;
        AnimatePillWidth(Math.Max(IslandIdleDip, content));
    }

    /// <summary>Hides a satellite for the drag chrome; the face pipeline re-shows it on exit.</summary>
    private static void CollapseIslandSatellite(Border host)
    {
        // Tag=false marks it hidden so a late HideSatelliteAnimated completion cannot re-show it
        // and the next ApplySatellites restores it through the normal animated path.
        host.Tag = false;
        if (host.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        host.BeginAnimation(UIElement.OpacityProperty, null);
        host.Opacity = 1;
        host.Visibility = Visibility.Collapsed;
        host.Margin = new Thickness(0);
    }

    /// <summary>
    /// Width of the badge in the caption's typography. A probe excludes the live caption's
    /// 8 DIP gap margin, which the drag width formula adds on its own.
    /// </summary>
    private double MeasureIslandDragBadge()
    {
        var badge = new TextBlock
        {
            FontFamily = IslandIdleCaption.FontFamily,
            FontSize = IslandIdleCaption.FontSize,
            FontWeight = IslandIdleCaption.FontWeight,
            Text = IslandDragBadgeText,
        };
        return IslandTitleMeasure.Dip(badge);
    }

    /// <summary>One reused accent glow; only its opacity animates, and it detaches when idle.</summary>
    private void SetIslandDragGlow(double to)
    {
        DropShadowEffect glow = EnsureIslandDragGlow();
        double from = glow.Opacity;
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        glow.Opacity = to;
        double ms = IslandMotion.Gate(IslandMotion.ContentFadeMs, SystemParameters.ClientAreaAnimation);
        if (ms <= 0 || Math.Abs(from - to) < 0.001)
        {
            if (to <= 0)
            {
                DetachIslandDragGlow();
            }

            return;
        }

        var fade = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        if (to <= 0)
        {
            // The always-visible pill must not keep rendering through an effect surface.
            fade.Completed += (_, _) => DetachIslandDragGlow();
        }

        glow.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
    }

    /// <summary>Clears the effect once the exit fade has landed; a re-enter before that re-owns it.</summary>
    private void DetachIslandDragGlow()
    {
        if (_islandDragActive || _islandDragGlow is null)
        {
            return;
        }

        if (ReferenceEquals(IslandPill.Effect, _islandDragGlow))
        {
            IslandPill.Effect = null;
        }
    }

    private DropShadowEffect EnsureIslandDragGlow()
    {
        if (_islandDragGlow is null)
        {
            _islandDragGlow = new DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 14,
                Opacity = 0,
            };
        }

        // Follow the live accent resource on every enter instead of capturing it once.
        _islandDragGlow.Color = TryFindResource("AccentFillColorDefaultBrush") is SolidColorBrush accentBrush
            ? accentBrush.Color
            : TryFindResource("SystemAccentColor") is Color accentColor
                ? accentColor
                : Color.FromRgb(0x00, 0x78, 0xD4);
        if (!ReferenceEquals(IslandPill.Effect, _islandDragGlow))
        {
            IslandPill.Effect = _islandDragGlow;
        }

        return _islandDragGlow;
    }

    private void OnIslandPanelEnter(object sender, MouseEventArgs e) => SyncIslandPointer(IslandNow());

    private void OnIslandPanelLeave(object sender, MouseEventArgs e) => SyncIslandPointer(IslandNow());

    private void OnIslandPillClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ActivateIslandPill();
    }

    private void OnIslandPillPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right)
        {
            if (MoveSatelliteSelection(e.Key))
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Enter
            && _selectedSatellite != IslandSatellite.None
            && _presence is not null
            && Island is { Config.Enabled: true })
        {
            RunIslandCommand(_presence.ClickSatellite(_selectedSatellite, IslandNow()));
            return;
        }

        ActivateIslandPill();
    }

    /// <summary>
    /// Moves the pill selection across visible satellites. None is skipped.
    /// Returns false when the chip has no satellite, so the key is left alone.
    /// </summary>
    private bool MoveSatelliteSelection(Key key)
    {
        if (_presence is null || Island is not { Config.Enabled: true })
        {
            return false;
        }

        IslandSatellite[] visible = VisibleSatellites(_presence.View);
        if (visible.Length == 0)
        {
            return false;
        }

        int index = Array.IndexOf(visible, _selectedSatellite);
        if (key == Key.Right)
        {
            index = index < 0 ? 0 : Math.Min(visible.Length - 1, index + 1);
        }
        else
        {
            index = index < 0 ? visible.Length - 1 : Math.Max(0, index - 1);
        }

        _selectedSatellite = visible[index];
        ApplySatellites(_presence.View);
        return true;
    }

    private static IslandSatellite[] VisibleSatellites(IslandPresenceView snap)
    {
        if (snap.FirstSatellite == IslandSatellite.None)
        {
            return [];
        }

        if (snap.SecondSatellite == IslandSatellite.None
            || snap.SecondSatellite == snap.FirstSatellite)
        {
            return [snap.FirstSatellite];
        }

        return [snap.FirstSatellite, snap.SecondSatellite];
    }

    private void OnIslandFirstSatelliteClick(object sender, MouseButtonEventArgs e)
        => ActivateSatellite(first: true, e);

    private void OnIslandSecondSatelliteClick(object sender, MouseButtonEventArgs e)
        => ActivateSatellite(first: false, e);

    private void ActivateIslandPill()
    {
        if (_presence is null || Island is not { Config.Enabled: true })
        {
            return;
        }

        RunIslandCommand(_presence.Click(IslandNow()));
    }

    /// <summary>
    /// The island popup's window starts 12 DIP above the panel top (shadow pad) and therefore
    /// covers the pill's lower half. Those clicks used to land on the popup with no element to
    /// receive them — the pill could only be pinned/toggled by clicking its upper few DIP. Route
    /// the band over the pill to the pill's own activation.
    /// </summary>
    private void OnIslandHostPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IslandPopup.IsOpen || !_islandJoined)
        {
            return;
        }

        Point pointInHost = e.GetPosition(IslandHost);
        if (IsIslandPillScreenPoint(IslandHost.PointToScreen(pointInHost)))
        {
            e.Handled = true;
            ActivateIslandPill();
            return;
        }

        // A click in the transparent shadow pad is a light dismiss. The popup is its own
        // no-activate window, so the window-behind activation that normally closes a pinned
        // flyout never reaches the bar and the click used to be swallowed instead.
        if (!IslandHost.IsPointInsidePanel(pointInHost))
        {
            e.Handled = true;
            CloseIsland();
        }
    }

    /// <summary>
    /// Companion to the mouse-down forwarding: a real pill click also activates the bar window as
    /// a side effect of mouse activation, which is what keeps Esc (bar-level PreviewKeyDown)
    /// working. Doing that on mouse-up matters: activating on mouse-down changes the mouse
    /// capture owner, and the same physical click's up event then lands on the bar pill and runs
    /// the click a second time (pin immediately toggled back off).
    /// </summary>
    private void OnIslandHostPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IslandPopup.IsOpen || !_islandJoined)
        {
            return;
        }

        if (IsIslandPillScreenPoint(IslandHost.PointToScreen(e.GetPosition(IslandHost))))
        {
            e.Handled = true;
            Activate();
        }
    }

    /// <summary>True when a screen-space point (device pixels) lies on the island pill.</summary>
    private bool IsIslandPillScreenPoint(Point screenPoint)
    {
        try
        {
            Point origin = IslandPill.PointToScreen(new Point(0, 0));
            Point corner = IslandPill.PointToScreen(
                new Point(IslandPill.ActualWidth, IslandPill.ActualHeight));
            return screenPoint.X >= origin.X - 1 && screenPoint.X <= corner.X + 1
                && screenPoint.Y >= origin.Y - 1 && screenPoint.Y <= corner.Y + 1;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// <see cref="MouseButtonEventArgs.Handled"/> is marked so a satellite click does not land on the pill itself.
    /// </summary>
    private void ActivateSatellite(bool first, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_presence is null || Island is not { Config.Enabled: true })
        {
            return;
        }

        IslandSatellite satellite = first ? _presence.View.FirstSatellite : _presence.View.SecondSatellite;
        if (satellite == IslandSatellite.None)
        {
            return;
        }

        RunIslandCommand(_presence.ClickSatellite(satellite, IslandNow()));
    }

    private void RunIslandCommand(IslandPillCommand command)
    {
        if (_presence is null)
        {
            return;
        }

        // A click-driven close overrides the pin: the flag is refreshed on every click
        // (if the click did not close, the leftover flag does not produce an auto-close on a motor-pinned panel).
        _islandPrompterCloseOnce = _islandPrompterPinned;

        switch (command)
        {
            case IslandPillCommand.OpenVolumeFlyout:
                CloseIsland(animate: false);
                ToggleQuickSettings(QuickSettingsPage.Mixer, IslandPill);
                ApplyIslandChrome(focusMusic: false);
                return;
            case IslandPillCommand.OpenBrightnessFlyout:
                CloseIsland(animate: false);
                OpenBrightnessFromPill();
                ApplyIslandChrome(focusMusic: false);
                return;
        }

        bool focus = _presence.View.Pinned && _presence.View.Panel != IslandPanelBody.None;
        ApplyIslandChrome(focus);
    }

    /// <summary>Soft-saves the chosen hub tab (no schema bump). Primary monitor only.</summary>
    private void OnIslandHubTabChanged(object? sender, IslandHubTab tab)
    {
        if (!Monitor.IsPrimary)
        {
            return;
        }

        IslandConfig cfg = _config.Current.Island;
        string value = IslandHub.ToConfig(tab);
        if (string.Equals(cfg.HubTab, value, StringComparison.Ordinal))
        {
            return;
        }

        cfg.HubTab = value;
        _config.Save(_config.Current);
    }

    /// <summary>
    /// Panel opening picks the tab. If the last face was <see cref="IslandPillFace.Drop"/> the shelf body
    /// comes up (while <see cref="IslandConfig.ShelfTab"/> is on): if a file was dropped the user
    /// wants to see the drop in the shelf. If the Shelf or Color or Prompter or Camera tab
    /// is off, the saved tab resolves to Clipboard while the fallback order stays the same (Clipboard → Wallpaper
    /// → Music; Clipboard if unknown). Likewise the secondary monitor carries Music only.
    /// </summary>
    private IslandHubTab ResolveHubTab(bool musicAvailable)
    {
        if (!Monitor.IsPrimary)
        {
            return IslandHubTab.Music;
        }

        IslandConfig cfg = _config.Current.Island;
        // A file drag hovering the pill previews the shelf: that is where a drop lands, and the
        // badge already promises it. The saved tab is untouched (only the preview shows Shelf).
        if (_islandDragActive && cfg.ShelfTab)
        {
            return IslandHubTab.Shelf;
        }

        if (_shownFace == IslandPillFace.Drop && cfg.ShelfTab)
        {
            return IslandHubTab.Shelf;
        }

        IslandHubTab tab = IslandHub.Resolve(cfg.HubTab, musicAvailable);
        return tab == IslandHubTab.Shelf && !cfg.ShelfTab ? IslandHubTab.Clipboard
            : tab == IslandHubTab.ColorPicker && !cfg.ColorPickerTab ? IslandHubTab.Clipboard
            : tab == IslandHubTab.Prompter && !cfg.PrompterTab ? IslandHubTab.Clipboard
            : tab == IslandHubTab.Camera && !cfg.CameraTab ? IslandHubTab.Clipboard
            : tab;
    }

    /// <summary>Flash target of the Color body after an accepted or rejected pick.</summary>
    private bool? _pendingPickerWritten;

    private ColorPickerWindow? _screenPicker;

    /// <summary>
    /// "Pick screen color": the island panel CLOSES, the overlay covers the work area (the single overlay
    /// exception the user approved), and the hub is presented again once the pick is done. While the overlay
    /// is open no other topmost window opens.
    /// </summary>
    private void OnIslandScreenPickerRequested(object? sender, EventArgs e)
    {
        if (!Monitor.IsPrimary)
        {
            return;
        }

        _pendingPickerWritten = null;
        CloseIsland(animate: false);
        StartScreenPicker();
    }

    /// <summary>
    /// Camera on/off single Apply path: catalog record + the host's single-owner
    /// Apply + save. The folder gallery applies the same three-step sequence; the body side only
    /// raises the <see cref="IslandFlyout.CameraMirrorRequested"/> event. The answer
    /// returns to the body and the pill face via <see cref="CameraMirrorHost.Updated"/>.
    /// </summary>
    private void OnIslandCameraMirrorRequested(object? sender, bool enable)
    {
        PluginCatalog.SetEnabled(_config.Current, PluginCatalog.CameraMirror, enable);
        CameraMirrorHost.Apply(_config.Current.Island.CameraMirror);
        _config.Save(_config.Current);
    }

    private void StartScreenPicker()
    {
        IntPtr hwnd = _source?.Handle ?? IntPtr.Zero;
        // IsOpen drops to false during the "settling" interval after a pick (0.9-1.6 s);
        // IsLoaded stays true until the window closes — which also forbids a second overlay in that interval.
        if (hwnd == IntPtr.Zero || _screenPicker is { IsLoaded: true })
        {
            return;
        }

        _screenPicker = new ColorPickerWindow();
        _screenPicker.Settled += OnScreenPickerSettled;
        _screenPicker.Cancelled += OnScreenPickerCancelled;
        _screenPicker.Closed += OnScreenPickerWindowClosed;
        if (!_screenPicker.Start(hwnd, _engine.Island))
        {
            // The overlay could not open: present the hub again and return to the user's context.
            _screenPicker = null;
            ReopenIslandAfterPicker(pickerWritten: null);
        }
    }

    private void OnScreenPickerSettled(Color color, string hex, bool written)
    {
        if (color.A != 0)
        {
            IslandPickedColors.Add(color);
        }

        _pendingPickerWritten = written;
    }

    private void OnScreenPickerCancelled()
    {
        // Reopen is done from a single point in the window's Closed (Cancelled also arrives before Close).
        _pendingPickerWritten = null;
    }

    /// <summary>
    /// Reopens the hub after the overlay closes: because the saved tab is "colorpicker" the
    /// body comes up in Color. <see cref="_pendingPickerWritten"/> carries only the pick result
    /// (null: cancelled); the flash is written at the stop after the body is presented.
    /// </summary>
    private void OnScreenPickerWindowClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _screenPicker))
        {
            return;
        }

        _screenPicker = null;
        ReopenIslandAfterPicker(_pendingPickerWritten);
    }

    private void ReopenIslandAfterPicker(bool? pickerWritten)
    {
        if (_presence is null)
        {
            return;
        }

        RunIslandCommand(_presence.Click(IslandNow()));
        if (pickerWritten is null)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => IslandHost.ShowPickerCopyFeedback(pickerWritten.Value)));
    }

    /// <summary>
    /// Session ended while the Music tab is showing: fall back to the saved tab (Clipboard).
    /// </summary>
    private void FallBackFromMusicTab()
    {
        if (!IslandPopup.IsOpen
            || IslandHost.CurrentHubTab != IslandHubTab.Music
            || _engine.Media.Snapshot.HasSession
            || !Monitor.IsPrimary)
        {
            return;
        }

        IslandHost.PresentHub(ResolveHubTab(musicAvailable: false), focus: false, crossfade: true);
    }

    private void OnIslandBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_presence is not null)
        {
            ApplyIslandFace(_presence.View);
        }
    }

    private void OnIslandClockTick(object? sender, EventArgs e)
    {
        _islandClock?.Stop();
        if (_presence is null)
        {
            return;
        }

        // Popup opening can raise a fake MouseLeave on the pill even though the cursor never
        // left the island (the popup window sits over the pill's lower edge). The presence
        // engine gets a geometry-confirmed pointer sample instead of IsMouseOver, which is
        // false exactly in that case.
        long now = IslandNow();
        (bool overPill, bool overPanel) = SampleIslandPointer();
        _presence.SamplePointer(overPill, overPanel, now);
        _presence.Tick(now);
        // Unconditional: ApplyIslandChrome re-arms the one-shot clock through ArmIslandClock.
        // A conditional repaint would leave the clock stopped if the timer fired a hair early
        // and no deadline was due yet, so the pending deadline would never fire again.
        ApplyIslandChrome(focusMusic: false);
    }

    private void OnIslandBrightnessChanged(object? sender, EventArgs e) => NoteIslandBrightness(apply: true);

    private void NoteIslandVolume()
    {
        if (_presence is null)
        {
            return;
        }

        VolumeSnapshot snapshot = _engine.Volume.Snapshot;
        IslandPresenceView before = _presence.View;
        long? deadline = _presence.NextDeadlineMs;
        _presence.ObserveVolume(
            snapshot.Percent,
            snapshot.IsMuted,
            snapshot.IsAvailable,
            IslandMixerOwnsVolume(),
            IslandNow());
        if (before != _presence.View || deadline != _presence.NextDeadlineMs)
        {
            ApplyIslandChrome(focusMusic: false);
        }
    }

    private void NoteIslandBrightness(bool apply)
    {
        if (_presence is null)
        {
            return;
        }

        if (_engine.MonitorBrightness.AnnouncedBrightness is not int percent)
        {
            return;
        }

        IslandPresenceView before = _presence.View;
        long? deadline = _presence.NextDeadlineMs;
        _presence.ObserveBrightness(percent, BrightnessPopup.IsOpen, IslandNow());
        if (apply && (before != _presence.View || deadline != _presence.NextDeadlineMs))
        {
            ApplyIslandChrome(focusMusic: false);
        }
    }

    private void NoteIslandMedia(bool apply)
    {
        if (_presence is null)
        {
            return;
        }

        MediaSnapshot snapshot = _engine.Media.Snapshot;
        string pill = string.IsNullOrEmpty(snapshot.Title) ? snapshot.AppName : snapshot.Title;
        IslandPresenceView before = _presence.View;
        long? deadline = _presence.NextDeadlineMs;
        _presence.ObserveMedia(
            new IslandMediaCue(
                snapshot.HasSession,
                snapshot.SessionId,
                snapshot.Title,
                snapshot.Artist,
                snapshot.IsPlaying,
                pill),
            IslandNow());
        if (apply && (before != _presence.View || deadline != _presence.NextDeadlineMs))
        {
            ApplyIslandChrome(focusMusic: false);
        }

        FallBackFromMusicTab();
        ApplyIslandContentChrome(_presence.View.Face);
        // A track/session change can arrive without a presence-view change (same face, new cover);
        // the collapsed pill's album tint still has to follow it, including the session-end restore.
        UpdateClosedIslandPillFill();
    }

    private void NoteIslandBattery(bool apply)
    {
        if (_presence is null)
        {
            return;
        }

        StatusSnapshot snapshot = _engine.Status.Snapshot;
        IslandPresenceView before = _presence.View;
        long? deadline = _presence.NextDeadlineMs;
        _presence.ObserveBattery(
            snapshot.IsBatteryPresent,
            snapshot.IsBatteryCharging,
            snapshot.BatteryPercent,
            IslandNow());
        if (apply && (before != _presence.View || deadline != _presence.NextDeadlineMs))
        {
            ApplyIslandChrome(focusMusic: false);
        }
    }

    private bool IslandMixerOwnsVolume()
        => QuickSettingsPopup.IsOpen && QuickSettingsHost.IsMixerPage;

    private void OpenBrightnessFromPill()
    {
        if (BrightnessPopup.IsOpen)
        {
            CloseBrightness();
            return;
        }

        CancelPendingClose(BrightnessHost, ref _brightnessClosePending);
        CloseQuickSettings();
        CloseMedia();
        BrightnessPopup.PlacementTarget = IslandPill;
        BrightnessHost.Refresh();
        BrightnessPopup.IsOpen = true;
    }

    private void ApplyIslandChrome(bool focusMusic)
    {
        if (_presence is null)
        {
            return;
        }

        IslandPresenceView snap = _presence.View;
        if (!SyncIslandVisibility(snap))
        {
            ApplyCaptureAffinity(snap, pillShown: false);
            SyncIslandHoverWatch();
            return;
        }

        ApplyIslandFace(snap);
        SyncIslandPopup(snap, focusMusic);
        ApplyCaptureAffinity(snap, pillShown: true);
        SyncIslandHoverWatch();
        // The collapsed capsule follows the album cover (joined fill is owned by the popup path).
        UpdateClosedIslandPillFill();
        ArmIslandClock();
    }

    private bool SyncIslandVisibility(IslandPresenceView snap)
    {
        if (!IslandShouldShow(snap))
        {
            if (IslandPill.Visibility != Visibility.Collapsed)
            {
                IslandPill.Visibility = Visibility.Collapsed;
            }

            _shownFace = null;

            if (IslandPopup.IsOpen)
            {
                CloseIslandPopupOnly(animate: false);
            }

            _islandClock?.Stop();
            return false;
        }

        if (IslandPill.Visibility != Visibility.Visible)
        {
            IslandPill.Visibility = Visibility.Visible;
        }

        return true;
    }

    /// <summary>
    /// The closed island collapses on every bar. The primary also stays while idle when it is on.
    /// The secondary is visible only while there is music, a counter or an open panel.
    /// </summary>
    private bool IslandShouldShow(IslandPresenceView snap)
    {
        if (Island is not { Config.Enabled: true })
        {
            return false;
        }

        if (Monitor.IsPrimary)
        {
            return true;
        }

        return snap.Face != IslandPillFace.Idle || snap.Panel != IslandPanelBody.None;
    }

    /// <summary>Now-playing cover/thumbnail chrome. The pill surface tint lives on the panel body.</summary>
    private void ApplyIslandContentChrome(IslandPillFace face)
    {
        // Reached directly (media, snapshot pushes) as well as from ApplyIslandFace, so the
        // drag freeze has to hold here too: it owns glyph/cover.
        if (_islandDragActive)
        {
            return;
        }

        MediaSnapshot snapshot = _engine.Media.Snapshot;
        bool showCover = face == IslandPillFace.NowPlaying && snapshot.Thumbnail is not null;
        IslandCoverImage.Source = showCover ? snapshot.Thumbnail : null;
        IslandCoverThumb.Visibility = showCover ? Visibility.Visible : Visibility.Collapsed;
        IslandGlyph.Visibility = showCover ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PushColorMode(IslandConfig config)
    {
        IslandHost.ColorMode = IslandNormalizer.NormalizeColorMode(config.ColorMode);
        // album <-> system/neutral retints (or restores) the collapsed capsule right away.
        UpdateClosedIslandPillFill();
    }

    private void ApplyIslandFace(IslandPresenceView snap)
    {
        // Drag chrome owns the pill while a file drag hovers it; the presence clock and
        // snapshot changes must not repaint the glyph/caption underneath. On collapse the
        // normal pipeline runs again and applies whatever the face became meanwhile.
        if (_islandDragActive)
        {
            return;
        }

        ApplyIslandContentChrome(snap.Face);
        IslandGlyph.Text = snap.Face switch
        {
            IslandPillFace.NowPlaying => GlyphMusic,
            IslandPillFace.Volume => VolumeHudGlyph(snap.MeterPercent, snap.MeterMuted),
            IslandPillFace.Brightness => GlyphBrightness,
            IslandPillFace.Battery => ResolveBatteryGlyph(_engine.Status.Snapshot),
            IslandPillFace.Drop => GlyphDrop,
            IslandPillFace.Bluetooth => GlyphBluetooth,
            IslandPillFace.Camera => GlyphCamera,
            _ => GlyphClipboard,
        };

        if (_shownFace != snap.Face)
        {
            bool first = _shownFace is null;
            _shownFace = snap.Face;
            if (!first)
            {
                // Face swap: content dips and settles, the glyph pops in slightly. Both finish on
                // their own (FillBehavior.Stop) and never run while the pill sits idle.
                if (IslandGlyph.RenderTransform is not ScaleTransform glyphScale)
                {
                    glyphScale = new ScaleTransform(1, 1);
                    IslandGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
                    IslandGlyph.RenderTransform = glyphScale;
                }

                FadeFrom(
                    IslandContent,
                    IslandMotion.ContentFromOpacity,
                    IslandMotion.Gate(IslandMotion.ContentFadeMs, SystemParameters.ClientAreaAnimation),
                    glyphScale,
                    0.8);
            }
        }

        string tip = snap.Face switch
        {
            IslandPillFace.NowPlaying => BuildMediaTooltip(_engine.Media.Snapshot),
            IslandPillFace.Volume => snap.MeterMuted ? Strings.Island_VolumeMuted : string.Format(CultureInfo.CurrentCulture, Strings.Island_VolumeFormat, snap.MeterPercent),
            IslandPillFace.Brightness => string.Format(CultureInfo.CurrentCulture, Strings.Island_BrightnessFormat, snap.MeterPercent),
            IslandPillFace.Battery => BatteryTip(snap.MeterPercent),
            IslandPillFace.Clip or IslandPillFace.Drop => string.IsNullOrWhiteSpace(snap.Title)
                ? Strings.Island_ClipboardAndDrops
                : snap.Title,
            IslandPillFace.Bluetooth => string.IsNullOrWhiteSpace(snap.Title) ? "Bluetooth" : snap.Title,
            IslandPillFace.Camera => snap.Title,
            _ => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_ClipboardAndDrops : snap.Title,
        };
        if (!string.Equals(tip, _islandTip, StringComparison.Ordinal))
        {
            _islandTip = tip;
            SetPlainToolTip(IslandPill, tip);
        }

        AutomationProperties.SetName(IslandPill, IslandAccessName(snap));

        double satelliteDip = ApplySatellites(snap);
        bool meter = snap.Face is IslandPillFace.Volume or IslandPillFace.Brightness or IslandPillFace.Battery;
        bool showTitle = false;
        double titleDip = 0;

        if (meter)
        {
            ShowMeter();
            double fill = IslandMeterDip * Math.Clamp(snap.MeterPercent, 0, 100) / 100d;
            if (snap.Face == IslandPillFace.Volume && snap.MeterMuted)
            {
                fill = 0;
            }

            AnimateWidth(IslandMeterFill, FrameworkElement.WidthProperty, fill, ref _meterFill);
        }
        else
        {
            HideMeter();
            showTitle = TryShowTitle(snap, satelliteDip, out titleDip);
            if (!showTitle)
            {
                ShowIdleChrome(snap.Face == IslandPillFace.Idle);
            }
        }

        double extras = ApplyIslandExtras(snap);
        double content = meter
            ? IslandPadDip + IslandGlyphDip + 8 + IslandMeterDip + satelliteDip
            : showTitle
                ? IslandPadDip + IslandGlyphDip + 8 + titleDip + extras + satelliteDip
                : IslandIdleDip + extras + satelliteDip;
        // The hover allowance is geometry-based: an OLE drag freezes IsMouseOver, and the panel
        // popup can take the pointer from the pill; a raw IsMouseOver would flicker the width.
        double hover = !_islandDragActive && IsPointerOverIsland() ? IslandHoverDip : 0;
        AnimatePillWidth(Math.Max(IslandIdleDip, content) + hover);
    }

    private bool TryShowTitle(
        IslandPresenceView snap,
        double satelliteDip,
        out double titleDip)
    {
        titleDip = 0;
        if (!FaceHasTitle(snap.Face) || string.IsNullOrWhiteSpace(snap.Title))
        {
            return false;
        }

        bool sameLayout = snap.Title == _faceTitle
            && Math.Abs(_faceBar - ActualWidth) < 1
            && Math.Abs(_faceLeft - LeftCluster.ActualWidth) < 1
            && Math.Abs(_faceRight - StatusStrip.ActualWidth) < 1
            && Math.Abs(_faceSatellites - satelliteDip) < 1
            && IslandTitle.Visibility == Visibility.Visible;
        if (!sameLayout)
        {
            _faceTitle = snap.Title;
            _faceBar = ActualWidth;
            _faceLeft = LeftCluster.ActualWidth;
            _faceRight = StatusStrip.ActualWidth;
            _faceSatellites = satelliteDip;

            IslandTitle.Text = snap.Title;
            IslandTitle.BeginAnimation(FrameworkElement.MaxWidthProperty, null);
            IslandTitle.MaxWidth = 1000;
            _titleWidth = IslandPresence.TitleMaxWidth(
                ActualWidth,
                LeftCluster.ActualWidth + IslandContentInsetDip,
                StatusStrip.ActualWidth + IslandContentInsetDip,
                IslandTitleChromeDip + satelliteDip + IslandHoverDip,
                IslandTitleMeasure.Dip(IslandTitle));
        }

        if (_titleWidth <= 0)
        {
            return false;
        }

        titleDip = _titleWidth;
        IslandIdleCaption.Visibility = Visibility.Collapsed;
        IslandTitle.Visibility = Visibility.Visible;
        IslandTitle.Margin = new Thickness(8, 0, 0, 0);
        IslandTitle.BeginAnimation(FrameworkElement.MaxWidthProperty, null);
        IslandTitle.MaxWidth = titleDip;
        return true;
    }

    private void ShowIdleChrome(bool showLabel)
    {
        _faceTitle = string.Empty;
        _titleWidth = 0;
        IslandTitle.BeginAnimation(FrameworkElement.MaxWidthProperty, null);
        IslandTitle.MaxWidth = 0;
        IslandTitle.Margin = new Thickness(0);
        IslandTitle.Visibility = Visibility.Collapsed;
        IslandIdleCaption.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMeter()
    {
        ShowIdleChrome(showLabel: false);
        IslandMeter.BeginAnimation(FrameworkElement.WidthProperty, null);
        IslandMeter.Width = IslandMeterDip;
        IslandMeter.Margin = new Thickness(8, 0, 0, 0);
        IslandMeter.Visibility = Visibility.Visible;
    }

    private void HideMeter()
    {
        IslandMeter.BeginAnimation(FrameworkElement.WidthProperty, null);
        IslandMeter.Width = 0;
        IslandMeter.Margin = new Thickness(0);
        IslandMeter.Visibility = Visibility.Collapsed;
        IslandMeterFill.BeginAnimation(FrameworkElement.WidthProperty, null);
        IslandMeterFill.Width = 0;
        _meterFill = 0;
    }

    private void AnimatePillWidth(double to)
    {
        if (double.IsNaN(_pillWidth))
        {
            _pillWidth = to;
            IslandPill.BeginAnimation(FrameworkElement.WidthProperty, null);
            IslandPill.Width = to;
            return;
        }

        AnimateWidth(IslandPill, FrameworkElement.WidthProperty, to, ref _pillWidth);
    }

    private static void AnimateWidth(FrameworkElement element, DependencyProperty property, double to, ref double cached)
    {
        if (Math.Abs(cached - to) < 0.5)
        {
            return;
        }

        // Retarget from the value the user currently sees (an animation may be mid-flight),
        // so a quick second change glides on instead of snapping back to the old target.
        double from = element.GetValue(property) is double live && !double.IsNaN(live) ? live : cached;
        cached = to;
        element.BeginAnimation(property, null);
        element.SetValue(property, to);
        double ms = IslandMotion.WidthMs(from, to, SystemParameters.ClientAreaAnimation);
        if (ms <= 0)
        {
            return;
        }

        // FillBehavior.Stop: the local value is already `to`, so nothing is held after the
        // clock ends and no animation clock lingers on an idle pill.
        element.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = to > from
                ? new QuinticEase { EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    /// <summary>One-shot opacity (and optional scale) animation that leaves nothing running.</summary>
    private static void FadeFrom(UIElement element, double fromOpacity, double ms, ScaleTransform? scale = null, double fromScale = 1)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (ms <= 0)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(fromOpacity, 1, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        });
        if (scale is null)
        {
            return;
        }

        var grow = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private ScaleTransform EnsurePillScale()
    {
        if (IslandPill.RenderTransform is ScaleTransform existing)
        {
            return existing;
        }

        var transform = new ScaleTransform(1, 1);
        IslandPill.RenderTransformOrigin = new Point(0.5, 0.5);
        IslandPill.RenderTransform = transform;
        return transform;
    }

    private void OnIslandPillPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_islandPressed || IsInsideSatellite(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _islandPressed = true;
        PressPill(true);
        // Release can land outside the pill; MouseUp on the pill, leave, or capture loss undoes it.
        IslandPill.MouseLeftButtonUp += ReleasePillPress;
        IslandPill.MouseLeave += ReleasePillPress;
    }

    private void ReleasePillPress(object sender, MouseEventArgs e)
    {
        IslandPill.MouseLeftButtonUp -= ReleasePillPress;
        IslandPill.MouseLeave -= ReleasePillPress;
        if (!_islandPressed)
        {
            return;
        }

        _islandPressed = false;
        PressPill(false);
    }

    private bool IsInsideSatellite(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, IslandPill))
        {
            if (ReferenceEquals(source, IslandSatelliteFirst) || ReferenceEquals(source, IslandSatelliteSecond))
            {
                return true;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private void PressPill(bool down)
    {
        ScaleTransform scale = EnsurePillScale();
        double to = down ? IslandMotion.PressScale : 1;
        double from = scale.ScaleX;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        // Removing the clock cancels an in-flight pulse, whose Completed handler would otherwise
        // be the only writer to clear the gate; without this reset the pill never pulses again.
        _islandPulseRunning = false;
        scale.ScaleX = to;
        scale.ScaleY = to;
        double ms = IslandMotion.Gate(down ? IslandMotion.PressInMs : IslandMotion.PressOutMs, SystemParameters.ClientAreaAnimation);
        if (ms <= 0 || Math.Abs(from - to) < 0.001)
        {
            return;
        }

        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = down
                ? new CubicEase { EasingMode = EasingMode.EaseOut }
                : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
            FillBehavior = FillBehavior.Stop,
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private static string VolumeHudGlyph(int percent, bool muted)
    {
        if (muted || percent <= 0)
        {
            return "\uE74F";
        }

        if (percent < 33)
        {
            return "\uE992";
        }

        return percent < 66 ? "\uE993" : "\uE767";
    }

    private void SyncIslandPopup(IslandPresenceView snap, bool focusMusic)
    {
        if (!Monitor.IsPrimary && snap.Panel == IslandPanelBody.Clipboard)
        {
            _presence?.Dismiss(IslandNow());
            CloseIslandPopupOnly(animate: false);
            return;
        }

        if (snap.Panel == IslandPanelBody.None)
        {
            // Prompter playback pin: the engine's automatic close (hover-exit delay /
            // temporary hold finished) does not close the panel. An explicit close (CloseIsland calls
            // CloseIslandPopupOnly directly; a pill click arrives with the one-shot flag) does not come here.
            if (_islandPrompterPinned && IslandPopup.IsOpen && !_islandPrompterCloseOnce)
            {
                return;
            }

            _islandPrompterCloseOnce = false;
            _shownPanel = IslandPanelBody.None;
            _shownBack = false;
            if (IslandPopup.IsOpen)
            {
                CloseIslandPopupOnly(animate: true);
            }

            return;
        }

        if (!IslandPopup.IsOpen)
        {
            _islandFocusMusic = focusMusic;
            CloseQuickSettings();
            CloseBrightness();
            CloseMedia();
            CancelIslandMotion();
            SetIslandJoined(true);
            // Hidden until the arranged width is known. A pre-measure offset (the XAML
            // width) shifts the panel left of the pill; the first painted frame must not.
            IslandHost.Opacity = 0;
            _islandAwaitingReveal = true;
            _islandRevealPosted = false;
            IslandPopup.IsOpen = true;
            return;
        }

        // A close that is still animating (the pointer left, then came back — or a click pinned
        // the panel) must not win over this presentation: its Completed would set IsOpen=false
        // and the next chrome pass would re-open the panel, i.e. a close+reopen blink.
        if (_islandClosePending)
        {
            CancelIslandMotion();
        }

        if (_shownPanel == snap.Panel && _shownBack == snap.ShowClipboardBack)
        {
            if (focusMusic)
            {
                if (IslandHost.CurrentHubTab == IslandHubTab.Clipboard)
                {
                    IslandHost.FocusClipboardSearch();
                }
                else
                {
                    IslandHost.PresentHub(IslandHost.CurrentHubTab, focus: true);
                }
            }

            return;
        }

        PresentIslandBody(snap, focusMusic, crossfade: true);
        IslandHost.UpdateLayout();
        AlignIslandPopup(force: false);
    }

    private void PresentIslandBody(IslandPresenceView snap, bool focusMusic, bool crossfade = false)
    {
        _shownPanel = snap.Panel;
        _shownBack = snap.ShowClipboardBack;
        bool music = _engine.Media.Snapshot.HasSession;
        IslandHost.PresentHub(ResolveHubTab(music), focusMusic, crossfade);
    }

    private void CloseIsland(bool animate = true)
    {
        _presence?.Dismiss(IslandNow());

        CloseIslandPopupOnly(animate);
        ArmIslandClock();
    }

    private void CloseIslandPopupOnly(bool animate)
    {
        bool wasAwaitingReveal = _islandAwaitingReveal;
        _islandAwaitingReveal = false;
        _islandRevealPosted = false;
        if (!IslandPopup.IsOpen)
        {
            _islandClosePending = false;
            return;
        }

        if (animate)
        {
            // A close that is already animating is a no-op. The presence engine can emit
            // Panel=None on two passes (and an explicit close runs after the engine's own
            // close); falling through to the instant branch here tore the popup down mid-close,
            // so the panel vanished and the pill unjoined before the animation had finished.
            if (_islandClosePending)
            {
                return;
            }

            // Never revealed (the measured width was not known yet): hide it instead of
            // animating a visible frame — an animated close would first paint the panel at
            // full opacity for one frame.
            if (wasAwaitingReveal)
            {
                CancelIslandMotion();
                IslandPopup.IsOpen = false;
                return;
            }

            _islandClosePending = true;
            AnimateIsland(opening: false, () =>
            {
                if (!_islandClosePending)
                {
                    return;
                }

                _islandClosePending = false;
                IslandPopup.IsOpen = false;
            });
            return;
        }

        CancelIslandMotion();
        IslandPopup.IsOpen = false;
    }

    private void OnIslandPopupOpened(object sender, EventArgs e)
    {
        // A stale close (or a close that a quick re-open overtook) is dropped here — but the
        // reveal may already have started the open morph from the popup's first layout pass.
        // Cancelling that morph here is what made the panel snap open instead of growing.
        if (!_islandOpeningMotion)
        {
            CancelIslandMotion();
            if (_islandAwaitingReveal)
            {
                // CancelIslandMotion writes Opacity=1. The "hidden until arranged" guard must
                // survive until TryRevealIsland has actually aligned the panel: the reveal can be
                // deferred (measured width not known in the Opened pass), and a mispositioned
                // full-opacity first frame would flash before the grow starts.
                IslandHost.Opacity = 0;
            }
        }

        // The panel already shows the track; a pill tooltip that opens later would float over
        // the tab strip. Suppress it while the panel is open and restore it on close. The
        // hover that opens the panel (150 ms) can beat the tooltip's own delay, so an instance
        // that is already open has to be dismissed as well — SetIsEnabled alone only blocks
        // later ones.
        SetIslandToolTipsEnabled(false);
        CloseIslandToolTips();
        if (Monitor.IsPrimary && Island is { } island)
        {
            IslandHost.Attach(island);
        }

        if (_presence is not null)
        {
            PresentIslandBody(_presence.View, _islandFocusMusic);
        }

        TryRevealIsland();
    }

    private void OnIslandPopupClosed(object sender, EventArgs e)
    {
        _shownPanel = IslandPanelBody.None;
        _shownBack = false;
        _alignedPillCenterPx = null;
        SyncIslandHoverWatch();
        IslandHost.OnPanelClosed();
        SetIslandJoined(false);
        SetIslandToolTipsEnabled(true);
        // A close can land while the drag is still on the pill (for example a cancel/close
        // animation that a quick re-enter raced). Re-arm the preview so the drag keeps its
        // drop target instead of showing chrome over a closed island.
        if (_islandDragActive && _islandDragValid)
        {
            ArmIslandDragPreview();
        }
    }

    /// <summary>
    /// Suppresses or restores the pill and satellite tooltips. A joined panel owns the space
    /// directly under the pill, so a tooltip opening there would cover the hub tab strip.
    /// </summary>
    private void SetIslandToolTipsEnabled(bool enabled)
    {
        foreach (FrameworkElement host in IslandToolTipHosts())
        {
            ToolTipService.SetIsEnabled(host, enabled);
        }
    }

    /// <summary>
    /// Dismisses a tooltip that is already on screen when the panel opens. The panel's 150 ms
    /// hover can win the race against the tooltip's own delay, and disabling the service does not
    /// close an instance that is already visible.
    /// </summary>
    private void CloseIslandToolTips()
    {
        foreach (FrameworkElement host in IslandToolTipHosts())
        {
            if (host.ToolTip is ToolTip tip && tip.IsOpen)
            {
                tip.IsOpen = false;
            }
        }
    }

    private FrameworkElement[] IslandToolTipHosts() =>
        [IslandPill, IslandSatelliteFirst, IslandSatelliteSecond];

    /// <summary>
    /// Last line of defence for the tooltip race: a tooltip that WPF already asked to open while
    /// the panel is joined is refused. <see cref="ToolTipService.SetIsEnabled"/> only covers the
    /// requests that come after it.
    /// </summary>
    private void OnIslandToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (_islandJoined || IslandPopup.IsOpen)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Prompter playback pin: while playback is on, the panel's automatic close is suppressed; once the pin
    /// is released the normal flow returns (in the Panel=None state suppressed while pinned the
    /// close is observed immediately). The explicit close (CloseIsland) is not touched; a pill click
    /// arrives with the one-shot flag.
    /// </summary>
    private void OnIslandPrompterPinnedChanged(bool pinned)
    {
        if (_islandPrompterPinned == pinned)
        {
            return;
        }

        _islandPrompterPinned = pinned;
        _islandPrompterCloseOnce = false;
        if (pinned)
        {
            // Pull back the auto-close pending at the moment of pinning: the closing animation is cancelled and
            // the panel stays on a fully open frame (same as the region's own CancelPendingClose principle).
            CancelIslandMotion();
            return;
        }

        // Without the pin the recorded panel may be None (it was suppressed while pinned): the normal flow closes it.
        if (_presence is not null && IslandPopup.IsOpen)
        {
            ApplyIslandChrome(focusMusic: false);
        }
    }

    /// <summary>
    /// Measure, then center, then fade in. Stays at opacity 0 while the arranged width is 0
    /// so the XAML width cannot paint a left-shifted first frame.
    /// </summary>
    private void TryRevealIsland()
    {
        if (!_islandAwaitingReveal || !IslandPopup.IsOpen || _islandMeasuring)
        {
            return;
        }

        _islandMeasuring = true;
        try
        {
            IslandHost.UpdateLayout();
        }
        finally
        {
            _islandMeasuring = false;
        }

        if (!AlignIslandPopup(force: true))
        {
            if (_islandRevealPosted)
            {
                return;
            }

            _islandRevealPosted = true;
            // Render runs before the next paint (Loaded is below it), so the panel is still at
            // Opacity=0 when the retry aligns it — no mispositioned frame can slip out.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (_islandAwaitingReveal && IslandPopup.IsOpen)
                {
                    TryRevealIsland();
                }
            }));
            return;
        }

        _islandAwaitingReveal = false;
        _islandRevealPosted = false;
        AnimateIsland(opening: true, onDone: null);
    }

    /// <summary>
    /// Placement=Bottom left-aligns the popup. Uses only the arranged panel width.
    /// Returns false when that width is not known yet.
    /// </summary>
    private bool AlignIslandPopup(bool force)
    {
        if (!IslandPopup.IsOpen && IslandPill.ActualWidth <= 0)
        {
            return false;
        }

        double panelDip = IslandHost.PanelWidthDip;
        double pillDip = IslandPill.ActualWidth;
        if (panelDip <= 1 || double.IsNaN(pillDip) || pillDip <= 1
            || _source is not { Handle: var hwnd } || hwnd == IntPtr.Zero)
        {
            return false;
        }

        Point origin;
        try
        {
            origin = IslandPill.PointToScreen(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        double dpi = Display.GetDpi(hwnd);
        RECT work = Display.GetMonitorWorkArea(hwnd);
        double offset = IslandPopupAlign.HorizontalOffset(
            pillDip,
            panelDip,
            origin.X,
            dpi,
            work.Left,
            work.Right);
        ApplyIslandOffset(offset - IslandFlyout.ShadowPadDip, force);
        IslandHost.SetJoin(pillDip, -offset);
        _joinPillWidthDip = pillDip;
        // Remember the center this offset was computed for: a later width change that keeps
        // the centered pill's center must not re-align (that made the panel jitter on hover).
        _alignedPillCenterPx = origin.X + (pillDip * dpi / 96d / 2d);
        return true;
    }

    /// <summary>
    /// Writes <c>HorizontalOffset</c>. On the first reveal, nudge then set so a value
    /// stored before the popup existed is actually applied. Later updates skip a no-op write.
    /// </summary>
    private void ApplyIslandOffset(double offset, bool force)
    {
        if (force)
        {
            IslandPopup.HorizontalOffset = offset + 1;
        }
        else if (Math.Abs(IslandPopup.HorizontalOffset - offset) <= 0.5)
        {
            return;
        }

        IslandPopup.HorizontalOffset = offset;
    }

    private void OnIslandPopupSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IslandPopup.IsOpen || !e.WidthChanged || _islandMeasuring)
        {
            return;
        }

        if (_islandAwaitingReveal)
        {
            TryRevealIsland();
            return;
        }

        AlignIslandPopup(force: false);
    }

    private void OnIslandPillSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IslandPopup.IsOpen || !e.WidthChanged || _islandMeasuring)
        {
            return;
        }

        if (_islandAwaitingReveal)
        {
            TryRevealIsland();
            return;
        }

        // The pill is centered on the bar, so a width change (hover allowance, title content)
        // keeps the pill's screen center and the panel's centered offset still lands on it.
        // Re-aligning on those hover-only changes was what nudged the panel under the pointer.
        double? center = IslandPillScreenCenterPx();
        if (_alignedPillCenterPx is double aligned
            && center is double current
            && Math.Abs(current - aligned) < 0.5)
        {
            // The panel must not move (the pill's center did not), but the pill's edges did.
            // The concave fillets are built from the pill's width and left edge, so a hover,
            // title or HUD width morph has to rebuild the join — otherwise the widened pill
            // outgrows the panel's top-edge notch and the merged look breaks.
            RefreshIslandJoin();
            return;
        }

        AlignIslandPopup(force: false);
    }

    /// <summary>
    /// Rebuilds the silhouette join for the pill's current width without moving the panel
    /// (<see cref="AlignIslandPopup"/> positions the popup; this only refreshes the notch).
    /// </summary>
    private void RefreshIslandJoin()
    {
        if (!_islandJoined || !IslandPopup.IsOpen || _islandAwaitingReveal)
        {
            return;
        }

        double pillDip = IslandPill.ActualWidth;
        double panelDip = IslandHost.PanelWidthDip;
        if (double.IsNaN(pillDip) || pillDip <= 1 || panelDip <= 1)
        {
            return;
        }

        // Panel-local X of the pill's left edge: the aligned offset includes the shadow-pad
        // compensation; the width delta splits evenly because the pill stays centered.
        double offset = IslandPopup.HorizontalOffset + IslandFlyout.ShadowPadDip;
        double delta = (pillDip - _joinPillWidthDip) / 2d;
        IslandHost.SetJoin(pillDip, -(offset + delta));
        _joinPillWidthDip = pillDip;
    }

    /// <summary>Screen X of the centered pill's middle in device pixels, or null when unconnected.</summary>
    private double? IslandPillScreenCenterPx()
    {
        try
        {
            return IslandPill.PointToScreen(new Point(IslandPill.ActualWidth / 2d, 0)).X;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Open: the pill's bottom corners square off into the panel and both share the flyout fill.
    /// Closed: the capsule style returns.
    /// </summary>
    private void SetIslandJoined(bool open)
    {
        _islandJoined = open;
        if (open)
        {
            IslandHost.ApplySurface();
            // One continuous surface: the pill fill is the same theme surface + album tint at the
            // panel's top-edge alpha, so the join line, the fillets and the tab strip resolve to
            // the same color. With no tint this is the plain flyout surface (previous behavior).
            IslandPill.Background = FlyoutSurface.CreateTinted(IslandHost.SurfaceTint, IslandFlyout.JoinTintAmount);
            IslandPill.CornerRadius = new CornerRadius(12, 12, 0, 0);
            IslandPill.BorderThickness = new Thickness(1, 1, 1, 0);
            // The fill is shared; the stroke must be too. A capsule-style Bar.PillStroke against
            // the panel's tinted top-lit rim produced a visible color break where the fillet
            // meets the pill side.
            ApplyIslandJoinRim();
            IslandHost.SetJoin(IslandPill.ActualWidth, IslandHost.PanelWidthDip > 0 ? (IslandHost.PanelWidthDip - IslandPill.ActualWidth) / 2d : 0);
            _joinPillWidthDip = IslandPill.ActualWidth;
            // The joined fill owns the capsule; force the closed cache to repaint on close.
            _closedPillTintState = ClosedPillTintState.Invalid;
            return;
        }

        IslandPill.ClearValue(Border.BackgroundProperty);
        IslandPill.ClearValue(Border.CornerRadiusProperty);
        IslandPill.ClearValue(Border.BorderThicknessProperty);
        IslandPill.ClearValue(Border.BorderBrushProperty);
        _closedPillTintState = ClosedPillTintState.Invalid;
        // Back to the collapsed indicator: restore the subtle session tint (or the style fill).
        UpdateClosedIslandPillFill();
    }

    /// <summary>
    /// Paints the joined pill's 1 DIP stroke with the panel rim's top color, so the fillet arc
    /// and the pill side meet without a color step. Re-applied when the album tint changes.
    /// </summary>
    private void ApplyIslandJoinRim()
    {
        // While a file drag hovers the pill the drag chrome's accent stroke owns the border; the
        // panel rim returns on the drag's exit (SetIslandDragActive(false) re-applies this).
        if (_islandDragActive)
        {
            return;
        }

        var brush = new SolidColorBrush(IslandHost.JoinRimColor);
        brush.Freeze();
        IslandPill.BorderBrush = brush;
    }

    /// <summary>
    /// Re-paints the joined panel surface from the current theme. The panel silhouette, the joined
    /// pill fill and the rim are single brushes built from the theme at open time, and the island
    /// popup is its own window (it never sees the bar's WM_THEMECHANGED / WM_SETTINGCHANGE), so a
    /// theme or accent switch under an open panel has to re-run this paint pass.
    /// </summary>
    private void RefreshJoinedIslandSurface()
    {
        if (!_islandJoined || !IslandPopup.IsOpen)
        {
            return;
        }

        IslandHost.ApplySurface();
        IslandHost.RefreshThemeRim();
        IslandHost.RefreshThemeRows();
        IslandPill.Background = FlyoutSurface.CreateTinted(IslandHost.SurfaceTint, IslandFlyout.JoinTintAmount);
        ApplyIslandJoinRim();
    }

    private void OnIslandSurfaceTintChanged(object? sender, EventArgs e)
    {
        if (_islandJoined && IslandPopup.IsOpen)
        {
            // Same fill + rim as the panel's top edge: the join is one surface, and when the tint
            // changes (track, tab, color mode) the pill follows on the same fade's completion.
            IslandPill.Background = FlyoutSurface.CreateTinted(IslandHost.SurfaceTint, IslandFlyout.JoinTintAmount);
            ApplyIslandJoinRim();
            return;
        }

        UpdateClosedIslandPillFill();
    }

    /// <summary>
    /// Closed pill fill: while a session is carried and <c>island.colorMode</c> is album, the
    /// capsule takes a subtle version of the cover tint so the indicator itself follows the album.
    /// Idle / clipboard-only states, the neutral and system color modes and the drag chrome keep
    /// the plain theme fill; the same extractor/snapshot as the panel is used, so pill and panel
    /// always agree (including the accent fallback when a session has no cover).
    /// </summary>
    private void UpdateClosedIslandPillFill()
    {
        if (_islandJoined || IslandPopup.IsOpen)
        {
            // The joined fill (SetIslandJoined / OnIslandSurfaceTintChanged) owns the pill.
            _closedPillTintState = ClosedPillTintState.Invalid;
            return;
        }

        Color? tint = ResolveClosedIslandTint();
        // Geometry-based hover: the island popup overlaps the pill's lower edge, so a raw
        // IsMouseOver can be false while the pointer still rests on the island (same reason the
        // width/hover allowance uses IsPointerOverIsland).
        bool hover = IsPointerOverIsland();
        IslandRgb? tintRgb = tint is Color tintColor ? new IslandRgb(tintColor.R, tintColor.G, tintColor.B) : null;
        // The bar palette can change under the capsule (wallpaper change on a transparent primary
        // bar); the cached fill is composited over the old plate, so the revision is part of the key.
        if (_closedPillTintState.Matches(tintRgb, hover, _barPaletteRevision))
        {
            return;
        }

        _closedPillTintState = new ClosedPillTintState(tintRgb, hover, _barPaletteRevision);
        if (tint is not Color c)
        {
            IslandPill.ClearValue(Border.BackgroundProperty);
            return;
        }

        // Base = the bar's own capsule fill for the current state (theme-following, or the
        // wallpaper-following plate in transparent mode), so the album hue is added on top of the
        // exact fill the style would have used. Hover keeps the palette's hover plate as its base
        // (the local Background suppresses the style's own hover trigger) and gets a slightly
        // stronger tint amount on top.
        string hoverKey = TopBarContrastPalette.PillFillHoverKey;
        string restingKey = TopBarContrastPalette.PillFillKey;
        if ((IslandPill.TryFindResource(hover ? hoverKey : restingKey) as SolidColorBrush)?.Color is not Color surface)
        {
            IslandPill.ClearValue(Border.BackgroundProperty);
            return;
        }

        // The amount is normalized by the plate alpha: the wallpaper-driven light-wallpaper plate
        // (~70% opaque white) dilutes a fixed 12% layer, so the same album would look weaker on
        // that bar than on a theme plate. See IslandPalette.ClosedPillAmount + IslandTintTests.
        IslandPill.Background = FlyoutSurface.CreateTintedOver(
            surface,
            c,
            IslandPalette.ClosedPillAmount(surface.A, hover));
    }

    /// <summary>Tint the closed pill follows, or null when there is nothing to follow.</summary>
    private Color? ResolveClosedIslandTint()
    {
        if (_islandDragActive || _presence is null || Island is not { Config.Enabled: true } island)
        {
            return null;
        }

        if (!string.Equals(
                IslandNormalizer.NormalizeColorMode(island.Config.ColorMode),
                IslandConfig.ColorModeAlbum,
                StringComparison.Ordinal))
        {
            return null;
        }

        MediaSnapshot snapshot = _engine.Media.Snapshot;
        if (!snapshot.HasSession)
        {
            return null;
        }

        // The panel's own resolver: album mode, the live cover, accent fallback without a cover.
        return IslandTintSource.Resolve(IslandConfig.ColorModeAlbum, snapshot.Thumbnail, musicPlaying: true);
    }

    /// <summary>
    /// Drops a scale or fade on both the popup child and the inner surface.
    /// The child must stay at identity or the next open inherits a shifted placement.
    /// </summary>
    private void CancelIslandMotion()
    {
        _islandOpeningMotion = false;
        CancelPendingClose(IslandHost, ref _islandClosePending);
        CancelPendingClose(IslandHost.PanelSurface, ref _islandClosePending);
        IslandHost.ClearValue(UIElement.RenderTransformProperty);
    }

    private void AnimateIsland(bool opening, Action? onDone)
    {
        double panel = Math.Max(1, IslandHost.PanelWidthDip);
        double pill = Math.Max(1, IslandPill.ActualWidth);
        double fromX = Math.Clamp(pill / panel, 0.18, 1);
        // Scale the inner surface only. Popup placement reads the child's transformed
        // top-left; scaling IslandHost and then centering on the full width shifts the
        // panel left of the pill, and that position sticks after the scale returns to 1.
        IslandHost.ClearValue(UIElement.RenderTransformProperty);
        bool enabled = SystemParameters.ClientAreaAnimation;
        TimeSpan motion = opening
            ? IslandMotion.MorphOpenMs(fromX, enabled)
            : IslandMotion.MorphCloseMs(fromX, enabled);
        // Popup.Opened is raised after the popup's first layout pass, and that pass can already
        // have started this morph (popup width known -> reveal). The flag lets the Opened
        // handler tell a live open apart from a stale close it has to cancel.
        _islandOpeningMotion = opening;
        Action? done = opening ? () => _islandOpeningMotion = false : onDone;
        AnimateChipFlyout(IslandHost.PanelSurface, opening, done, fromX, 0.05, IslandHost, motion);
    }

    private void ArmIslandClock()
    {
        if (_islandClock is null)
        {
            return;
        }

        _islandClock.Stop();
        if (_presence?.NextDeadlineMs is not long deadline)
        {
            return;
        }

        long delay = Math.Max(1, deadline - IslandNow());
        _islandClock.Interval = TimeSpan.FromMilliseconds(delay);
        _islandClock.Start();
    }

    /// <summary>
    /// Keeps a short repeating pointer sample alive while an unpinned (hover/transient) panel
    /// is open. The panel popup is a separate window that sits over the pill's lower edge, so
    /// it can take the pointer without a real leave and without a MouseEnter on the panel; a
    /// one-off event check cannot see that. Pinned panels and closed popups stop the watch.
    /// </summary>
    private void SyncIslandHoverWatch()
    {
        bool wanted = IslandPopup.IsOpen
            && _presence is { View.Panel: not IslandPanelBody.None, View.Pinned: false };
        if (!wanted)
        {
            if (_islandHoverWatch is { IsEnabled: true })
            {
                _islandHoverWatch.Stop();
            }

            return;
        }

        if (_islandHoverWatch is null)
        {
            _islandHoverWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _islandHoverWatch.Tick += OnIslandHoverWatchTick;
        }

        if (!_islandHoverWatch.IsEnabled)
        {
            _islandHoverWatch.Start();
        }
    }

    private void OnIslandHoverWatchTick(object? sender, EventArgs e)
    {
        // A close animation can finish without another chrome pass; the watch must not outlive
        // the popup it samples.
        if (!IslandPopup.IsOpen)
        {
            _islandHoverWatch?.Stop();
            return;
        }

        SyncIslandPointer(IslandNow());
    }

    private WallpaperTransitionRequest? BuildWallpaperTransitionRequest()
    {
        IslandConfig? cfg = Island?.Config;
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (cfg is null || hwnd == IntPtr.Zero)
        {
            return null;
        }

        RECT rc = Display.GetPrimaryMonitorBounds(hwnd);
        int widthPx = rc.Right - rc.Left;
        if (widthPx <= 0)
        {
            return null;
        }

        return new WallpaperTransitionRequest(
            cfg.WallpaperTransition,
            cfg.WallpaperRespectSystemAnimation,
            SystemParameters.ClientAreaAnimation,
            widthPx);
    }

    private void AttachIsland()
    {
        IslandService? island = Island;
        _presence = new IslandPresence();
        _islandClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
        _islandClock.Tick += OnIslandClockTick;
        SizeChanged += OnIslandBarSizeChanged;
        IslandPill.SizeChanged += OnIslandPillSizeChanged;
        IslandHost.SizeChanged += OnIslandPopupSizeChanged;
        IslandHost.HubTabChanged += OnIslandHubTabChanged;
        IslandHost.ScreenPickerRequested += OnIslandScreenPickerRequested;
        IslandHost.PrompterPinnedChanged += OnIslandPrompterPinnedChanged;
        IslandHost.CameraMirrorRequested += OnIslandCameraMirrorRequested;
        IslandHost.SurfaceTintChanged += OnIslandSurfaceTintChanged;
        IslandHost.AttachMedia(_engine.Media);
        if (Monitor.IsPrimary)
        {
            _wallpaper ??= new WallpaperService(WallpaperPaths.BundledRoot, WallpaperPaths.UserRoot);
            _wallpaper.TransitionRequest = BuildWallpaperTransitionRequest;
            _wallpaper.WallpaperApplied += OnWallpaperAppliedRefreshPalette;
            _wallpaper.AccentEnabled = () =>
            {
                try
                {
                    IslandConfig? cfg = Island?.Config;
                    return cfg?.WallpaperAccent ?? IslandConfig.DefaultWallpaperAccent;
                }
                catch (Exception)
                {
                    return IslandConfig.DefaultWallpaperAccent;
                }
            };
            if (Application.Current is App app && app.Config is { } appConfig)
            {
                var sync = new WallpaperThemeSync(appConfig, _wallpaper);
                _wallpaper.HubApplied = sync.Remember;
                app.SetWallpaperSync(sync);
            }

            IslandHost.AttachWallpaper(_wallpaper);
        }

        IslandHost.SetClipboardChrome(Monitor.IsPrimary);
        IslandPill.AllowDrop = Monitor.IsPrimary;
        // The popup is a separate HWND: the preview panel needs its own OLE registration to take
        // the drag when it follows the surface down into the opened shelf (primary only).
        IslandHost.AllowDrop = Monitor.IsPrimary;
        _engine.MonitorBrightness.BrightnessChanged += OnIslandBrightnessChanged;
        _presence.SetClipboardSurface(Monitor.IsPrimary, IslandNow());
        PushLowBattery(island?.Config);

        if (island is not null)
        {
            island.Changed += OnIslandChanged;
            if (Monitor.IsPrimary)
            {
                island.EntryAdded += OnIslandEntryAdded;
                _engine.Bluetooth.Changed += OnBluetoothNotice;
                SyncBluetoothWatch();
                CameraMirrorHost.Updated += OnCameraMirrorUpdated;
            }
        }

        NoteIslandVolume();
        NoteIslandBrightness(apply: false);
        NoteIslandMedia(apply: false);
        NoteIslandBattery(apply: false);
        UpdateIslandPill();
    }

    /// <summary>Hub gallery applied a wallpaper: the strip tone may have changed.</summary>
    private void OnWallpaperAppliedRefreshPalette(string path)
        => RefreshWallpaperTextContrast();

    private void DetachIsland()
    {
        SizeChanged -= OnIslandBarSizeChanged;
        IslandPill.SizeChanged -= OnIslandPillSizeChanged;
        IslandHost.SizeChanged -= OnIslandPopupSizeChanged;
        IslandHost.HubTabChanged -= OnIslandHubTabChanged;
        IslandHost.ScreenPickerRequested -= OnIslandScreenPickerRequested;
        IslandHost.PrompterPinnedChanged -= OnIslandPrompterPinnedChanged;
        IslandHost.CameraMirrorRequested -= OnIslandCameraMirrorRequested;
        IslandHost.SurfaceTintChanged -= OnIslandSurfaceTintChanged;
        IslandHost.DetachWallpaper();
        if (_wallpaper is not null)
        {
            _wallpaper.WallpaperApplied -= OnWallpaperAppliedRefreshPalette;
            _wallpaper.HubApplied = null;
        }

        if (Monitor.IsPrimary && Application.Current is App detachApp)
        {
            detachApp.SetWallpaperSync(null);
        }

        IslandHost.DetachMedia();
        _engine.MonitorBrightness.BrightnessChanged -= OnIslandBrightnessChanged;

        if (_islandClock is not null)
        {
            _islandClock.Stop();
            _islandClock.Tick -= OnIslandClockTick;
            _islandClock = null;
        }

        _engine.Bluetooth.Changed -= OnBluetoothNotice;
        _engine.Bluetooth.StopWatching();
        CameraMirrorHost.Updated -= OnCameraMirrorUpdated;
        if (_islandHoverWatch is not null)
        {
            _islandHoverWatch.Stop();
            _islandHoverWatch.Tick -= OnIslandHoverWatchTick;
            _islandHoverWatch = null;
        }

        if (_islandDragPreviewTimer is not null)
        {
            _islandDragPreviewTimer.Stop();
            _islandDragPreviewTimer.Tick -= OnIslandDragPreviewTick;
            _islandDragPreviewTimer = null;
        }

        if (_islandDragLeaveTimer is not null)
        {
            _islandDragLeaveTimer.Stop();
            _islandDragLeaveTimer.Tick -= OnIslandDragLeaveTick;
            _islandDragLeaveTimer = null;
        }

        if (Island is { } island)
        {
            island.Changed -= OnIslandChanged;
            island.EntryAdded -= OnIslandEntryAdded;
        }

        ClearCaptureAffinity();
        // No service left to follow: the capsule returns to the plain style fill.
        IslandPill.ClearValue(Border.BackgroundProperty);
        _closedPillTintState = ClosedPillTintState.Invalid;
        // A drag in flight cannot outlive the window: stop the preview glow and the owed drop
        // motion before the panel is detached.
        IslandHost.ShowShelfDropTarget(false);
        IslandHost.CancelShelfDrop();
        IslandHost.Detach();
        _presence = null;
        _islandDragActive = false;
        _islandDragValid = false;
        _islandDragOverPanel = false;
        _islandJoined = false;
        _islandPrompterPinned = false;
        _islandPrompterCloseOnce = false;
        _idleLabelSet = false;
        _appliedLowBattery = int.MinValue;
        _selectedSatellite = IslandSatellite.None;
        _idlePreviewSet = false;
        _appliedIdlePreview = null;
        _screenPicker?.Close();
    }

    private void OnIslandChanged(object? sender, EventArgs e) => UpdateIslandPill();

    private void OnIslandEntryAdded(object? sender, EventArgs e)
    {
        if (_presence is null || !Monitor.IsPrimary || Island is not { } island || island.Entries.Count == 0)
        {
            return;
        }

        IslandEntry newest = island.Entries[0];
        if (newest.Kind != IslandEntryKind.Text)
        {
            return;
        }

        _presence.NoteNewClip(IslandPruner.FormatPreview(newest.Text), IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void UpdateIslandPill()
    {
        IslandService? island = Island;
        if (island is not { Config.Enabled: true } || _presence is null)
        {
            IslandPill.Visibility = Visibility.Collapsed;
            if (IslandPopup.IsOpen)
            {
                CloseIsland(animate: false);
            }

            // If the island is closed the picker overlay must not stay under a stale mask.
            _screenPicker?.Close();
            _islandClock?.Stop();
            ApplyCaptureAffinity(default, pillShown: false);
            return;
        }

        IslandMusicMode mode = IslandNormalizer.IsTransientMusic(island.Config.MusicPresence)
            ? IslandMusicMode.Transient
            : IslandMusicMode.Persistent;
        _presence.SetMusicMode(mode, IslandNow());
        PushLowBattery(island.Config);
        PushIdleLabel(island.Config);
        PushColorMode(island.Config);
        if (Monitor.IsPrimary)
        {
            PushIdlePreview(island);
        }

        ApplyIslandChrome(focusMusic: false);
    }

    /// <summary>
    /// Resting pill pulse. <paramref name="swallow"/> adds the drop variant: a compress-release
    /// that reads as the pill swallowing the file, with a one-shot accent flash on the shared
    /// drag glow. Both variants share the <see cref="_islandPulseRunning"/> reentry gate.
    /// </summary>
    private void PulseIslandPill(bool swallow = false)
    {
        if (_islandPulseRunning || IslandPill.Visibility != Visibility.Visible)
        {
            return;
        }

        ScaleTransform transform = EnsurePillScale();
        if (!SystemParameters.ClientAreaAnimation)
        {
            // Static end state: the pill rests at 1 and no accent effect stays attached. The drag
            // chrome's exit fade may still be in flight, so the swallow detaches it here.
            if (swallow)
            {
                DetachIslandDragGlow();
            }

            return;
        }

        _islandPulseRunning = true;
        if (!swallow)
        {
            var pulse = new DoubleAnimation(1d, 1.06d, TimeSpan.FromMilliseconds(160))
            {
                AutoReverse = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            };
            pulse.Completed += (_, _) => _islandPulseRunning = false;
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);
            return;
        }

        // Compress under the pointer, release just past 1, settle: the pill "swallows" the drop.
        double compress = IslandSwallowCompressMs;
        double release = compress + IslandSwallowReleaseMs;
        double settle = release + IslandSwallowSettleMs;
        var swallowPulse = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        swallowPulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        swallowPulse.KeyFrames.Add(new EasingDoubleKeyFrame(
            IslandSwallowCompressScale,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(compress)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        swallowPulse.KeyFrames.Add(new EasingDoubleKeyFrame(
            IslandSwallowReleaseScale,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(release)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        swallowPulse.KeyFrames.Add(new EasingDoubleKeyFrame(
            1,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(settle)),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        swallowPulse.Completed += (_, _) => _islandPulseRunning = false;
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, swallowPulse);
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, swallowPulse);
        FlashIslandAccent(compress, release, settle);
    }

    /// <summary>
    /// Accent flash for the swallow: the shared drag glow dims with the compress, flashes back to
    /// its drag brightness on the release, then fades out and detaches so the always-visible pill
    /// never keeps rendering through an effect.
    /// </summary>
    private void FlashIslandAccent(double compressMs, double releaseMs, double settleMs)
    {
        DropShadowEffect glow = EnsureIslandDragGlow();
        // The drag chrome's exit fade may still own this opacity clock; the flash takes it over.
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        glow.Opacity = 0;
        double dim = IslandMotion.Gate(compressMs, SystemParameters.ClientAreaAnimation);
        double flash = IslandMotion.Gate(releaseMs, SystemParameters.ClientAreaAnimation);
        double end = IslandMotion.Gate(settleMs, SystemParameters.ClientAreaAnimation);
        if (end <= 0)
        {
            // Reduced motion: the static end state is the detached glow, never a lit effect.
            DetachIslandDragGlow();
            return;
        }

        var accent = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        accent.KeyFrames.Add(new EasingDoubleKeyFrame(
            IslandDragGlowOpacity,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        accent.KeyFrames.Add(new EasingDoubleKeyFrame(
            0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(dim)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        accent.KeyFrames.Add(new EasingDoubleKeyFrame(
            IslandDragGlowOpacity,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(flash)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        accent.KeyFrames.Add(new EasingDoubleKeyFrame(
            0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(end)),
            new CubicEase { EasingMode = EasingMode.EaseIn }));
        accent.Completed += (_, _) => DetachIslandDragGlow();
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, accent);
    }

    private void OnIslandDragOver(object sender, DragEventArgs e)
    {
        // Fires ~60 Hz: only the effect is refreshed here. Chrome changes are owned by the
        // DragEnter/DragLeave transitions, so an already-active chrome is left untouched.
        e.Effects = Monitor.IsPrimary && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnIslandPillDrop(object sender, DragEventArgs e) => DropOnIsland(e);

    /// <summary>
    /// Single file-drop path for both surfaces that can receive the drag: the pill and the open
    /// preview panel (the panel is the extension; the pill is the guaranteed target).
    /// </summary>
    private void DropOnIsland(DragEventArgs e)
    {
        e.Handled = true;
        bool landed = false;
        string? displayName = null;
        try
        {
            if (!Monitor.IsPrimary || !e.Data.GetDataPresent(DataFormats.FileDrop) || Island is not { } island)
            {
                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            {
                return;
            }

            foreach (string path in paths)
            {
                // A folder drops into the shelf too (B1): its name is not counted as "skipped". A folder path
                // with a trailing separator comes back empty from Path.GetFileName, so it is read with the separator trimmed.
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string name = Path.GetFileName(path.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (name.Length > 0)
                {
                    displayName = name;
                }
            }

            // Arm the shelf arrival animation before AddDroppedFiles: its Changed -> RebuildShelf
            // runs synchronously on the UI thread. The newest id before the drop lets the flyout
            // animate exactly the landed records even when it is detached and builds the shelf for
            // the first time; a panel that still has to open delays the chip motion until after
            // the reveal. No shelf tab means no shelf arrival animation is owed at all.
            long newestBefore = 0;
            foreach (IslandEntry entry in island.Entries)
            {
                if (entry.Id > newestBefore)
                {
                    newestBefore = entry.Id;
                }
            }

            if (island.Config.ShelfTab)
            {
                IslandHost.PrepareShelfDrop(
                    panelStillOpening: !IslandPopup.IsOpen,
                    newestEntryIdBefore: newestBefore);
            }

            // The feedback rests on the entry COUNTED by the service, not on the payload strings: a path
            // deleted or renamed during the drag produces no entry, so the HUD, the swallow and the
            // shelf arrival animation must not run in the "nothing was dropped" case.
            int added = island.AddDroppedFiles(paths);
            if (added <= 0)
            {
                IslandHost.CancelShelfDrop();
                return;
            }

            landed = true;
            if (displayName is not null)
            {
                _presence?.NoteFileDrop(displayName, IslandNow());
                ApplyIslandChrome(focusMusic: false);
            }
        }
        finally
        {
            // No DragLeave follows a drop; end on every path (empty or failed payload
            // included) so the pill can never stay stuck in drag chrome. A landed drop keeps
            // the preview panel: the new shelf entry is the visual result of the drop.
            EndIslandDrag(keepPanel: landed);
        }

        // Only reached when a record actually landed (`landed` comes from the service's count,
        // not from the payload): the collapse above released the drag chrome, so the swallow's
        // accent flash owns the shared glow from here.
        if (landed)
        {
            // A quick drag can land before the preview's open delay fired; open the shelf on the
            // result so the dropped entry is visible either way (the face is Drop now, which
            // resolves the Shelf tab under the pill).
            if (!IslandPopup.IsOpen
                && Island?.Config is { Enabled: true, ShelfTab: true }
                && _presence is not null)
            {
                _presence.BeginFileDrag(IslandNow());
                _presence.EndFileDrag(keepPanel: true, IslandNow());
                ApplyIslandChrome(focusMusic: false);
            }
            else if (IslandPopup.IsOpen && IslandHost.CurrentHubTab != IslandHubTab.Shelf)
            {
                // The landed records are not on screen (another hub body is open): do not carry an
                // owed arrival animation into a later, unrelated shelf rebuild.
                IslandHost.CancelShelfDrop();
            }

            PulseIslandPill(swallow: true);
        }
    }

    private void PushLowBattery(IslandConfig? config)
    {
        int percent = IslandNormalizer.NormalizeLowBatteryPercent(
            config?.LowBatteryPercent ?? IslandConfig.DefaultLowBatteryPercent);
        if (_appliedLowBattery == percent)
        {
            return;
        }

        _appliedLowBattery = percent;
        _presence?.SetLowBatteryPercent(percent, IslandNow());
    }

    private void PushIdleLabel(IslandConfig config)
    {
        IslandIdleLabel label = IslandNormalizer.IsLastClipIdle(config.IdleLabel)
            ? IslandIdleLabel.LastClip
            : IslandIdleLabel.ClipboardName;
        if (_idleLabelSet && _appliedIdleLabel == label)
        {
            return;
        }

        _idleLabelSet = true;
        _appliedIdleLabel = label;
        _presence?.SetIdleLabel(label, IslandNow());
    }

    private void PushIdlePreview(IslandService island)
    {
        string? preview = LatestTextPreview(island.Entries);
        if (_idlePreviewSet && string.Equals(_appliedIdlePreview, preview, StringComparison.Ordinal))
        {
            return;
        }

        _idlePreviewSet = true;
        _appliedIdlePreview = preview;
        _presence?.SetIdlePreview(preview, IslandNow());
    }

    private static string? LatestTextPreview(IReadOnlyList<IslandEntry> entries)
    {
        foreach (IslandEntry entry in entries)
        {
            if (entry.Kind == IslandEntryKind.Text)
            {
                return IslandPruner.FormatPreview(entry.Text);
            }
        }

        return null;
    }

    private double ApplySatellites(IslandPresenceView snap)
    {
        if (_selectedSatellite != snap.FirstSatellite && _selectedSatellite != snap.SecondSatellite)
        {
            _selectedSatellite = IslandSatellite.None;
        }

        double width = PaintSatellite(IslandSatelliteFirst, IslandSatelliteFirstGlyph, snap.FirstSatellite);
        width += PaintSatellite(IslandSatelliteSecond, IslandSatelliteSecondGlyph, snap.SecondSatellite);
        return width;
    }

    private double PaintSatellite(Border host, TextBlock glyph, IslandSatellite satellite)
    {
        string? text = satellite switch
        {
            IslandSatellite.Clipboard => GlyphClipboard,
            IslandSatellite.Music => GlyphMusic,
            _ => null,
        };
        if (text is null)
        {
            host.BorderBrush = Brushes.Transparent;
            if (host.Tag is true && host.Visibility == Visibility.Visible)
            {
                // Closing: fade and shrink, then collapse. The pill width already morphs
                // to the smaller target, so both run together. Re-showing cancels this.
                host.Tag = false;
                HideSatelliteAnimated(host);
            }
            else if (host.Tag is not false)
            {
                host.Visibility = Visibility.Collapsed;
                host.Margin = new Thickness(0);
            }

            return 0;
        }

        glyph.Text = text;
        bool wasShown = host.Tag is true && host.Visibility == Visibility.Visible;
        host.Margin = new Thickness(8, 0, 0, 0);
        if (!wasShown)
        {
            host.Tag = true;
            host.Visibility = Visibility.Visible;
            ShowSatelliteAnimated(host);
        }

        bool selected = satellite == _selectedSatellite;
        if (selected && TryFindResource("AccentFillColorDefaultBrush") is not null)
        {
            // Live reference: a selected satellite can stay on the pill across a theme/accent swap
            // with no repaint in between, and a captured accent brush would keep the old ring.
            host.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        }
        else if (selected)
        {
            host.BorderBrush = Brushes.DodgerBlue;
        }
        else
        {
            host.ClearValue(Border.BorderBrushProperty);
        }

        // A ToolTip instance (not a plain string) so an already-open satellite tooltip can be
        // dismissed when the panel opens over the spot where it would sit.
        SetPlainToolTip(host, satellite == IslandSatellite.Music ? Strings.Island_Music : Strings.Island_Clipboard);
        return IslandSatelliteDip;
    }

    private static ScaleTransform SatelliteScale(Border host)
    {
        if (host.RenderTransform is ScaleTransform existing)
        {
            return existing;
        }

        var transform = new ScaleTransform(1, 1);
        host.RenderTransform = transform;
        return transform;
    }

    private static void ShowSatelliteAnimated(Border host)
    {
        ScaleTransform scale = SatelliteScale(host);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = 1;
        scale.ScaleY = 1;
        host.Opacity = 1;
        double ms = IslandMotion.Gate(IslandMotion.SatelliteInMs, SystemParameters.ClientAreaAnimation);
        FadeFrom(host, 0, ms, scale, IslandMotion.SatelliteFromScale);
    }

    private static void HideSatelliteAnimated(Border host)
    {
        ScaleTransform scale = SatelliteScale(host);
        double ms = IslandMotion.Gate(IslandMotion.SatelliteOutMs, SystemParameters.ClientAreaAnimation);
        void Collapse()
        {
            // A re-show in the meantime flips Tag back to true and owns the host again.
            if (host.Tag is false)
            {
                host.Tag = null;
                host.Visibility = Visibility.Collapsed;
                host.Margin = new Thickness(0);
            }

            host.Opacity = 1;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        if (ms <= 0)
        {
            Collapse();
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(host.Opacity, 0, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        fade.Completed += (_, _) =>
        {
            if (host.Tag is false)
            {
                host.BeginAnimation(UIElement.OpacityProperty, null);
                Collapse();
            }
        };
        host.BeginAnimation(UIElement.OpacityProperty, fade);
        var shrink = new DoubleAnimation(scale.ScaleX, IslandMotion.SatelliteFromScale, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    private double ApplyIslandExtras(IslandPresenceView snap)
    {
        bool camera = snap.Face == IslandPillFace.Camera;
        IslandCamera.Visibility = camera ? Visibility.Visible : Visibility.Collapsed;
        IslandCamera.Source = camera ? CameraMirrorHost.Preview?.Frame : null;
        return camera && IslandCamera.Source is not null ? 44 : 0;
    }

    private void OnBluetoothNotice(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnBluetoothNotice(sender, e));
            return;
        }

        if (_presence is null || Island?.Config.BluetoothNotice != true)
        {
            return;
        }

        IReadOnlyList<BluetoothDeviceInfo> current = _engine.Bluetooth.Devices;
        foreach (string name in BluetoothConnect.NewlyConnected(_btPrevious, current, _btSeeded))
        {
            _presence.NoteBluetooth(name, IslandNow());
        }

        _btPrevious = current;
        _btSeeded = true;
        ApplyIslandChrome(focusMusic: false);
    }

    private void OnCameraMirrorUpdated(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnCameraMirrorUpdated(sender, e));
            return;
        }

        if (_presence is null || !Monitor.IsPrimary)
        {
            return;
        }

        CameraPreview? preview = CameraMirrorHost.Preview;
        _presence.SetCamera(preview is not null, preview?.Status, IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void SyncBluetoothWatch()
    {
        if (!Monitor.IsPrimary)
        {
            return;
        }

        if (Island?.Config.BluetoothNotice == true)
        {
            _engine.Bluetooth.WatchConnections();
            return;
        }

        _engine.Bluetooth.StopWatching();
    }

    private static bool FaceHasTitle(IslandPillFace face)
        => face is IslandPillFace.NowPlaying or IslandPillFace.Idle or IslandPillFace.Clip or IslandPillFace.Drop
            or IslandPillFace.Bluetooth or IslandPillFace.Camera;

    private string BatteryTip(int percent)
    {
        string tip = string.Format(CultureInfo.CurrentCulture, Strings.Island_BatteryFormat, percent);
        return _engine.Status.Snapshot.IsBatteryCharging ? string.Format(CultureInfo.CurrentCulture, Strings.Island_BatteryChargingFormat, tip) : tip;
    }

    private static string IslandAccessName(IslandPresenceView snap)
    {
        return snap.Face switch
        {
            IslandPillFace.NowPlaying => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_Music : snap.Title,
            IslandPillFace.Volume => snap.MeterMuted ? Strings.Island_VolumeMuted : string.Format(CultureInfo.CurrentCulture, Strings.Island_VolumeFormat, snap.MeterPercent),
            IslandPillFace.Brightness => string.Format(CultureInfo.CurrentCulture, Strings.Island_BrightnessFormat, snap.MeterPercent),
            IslandPillFace.Battery => string.Format(CultureInfo.CurrentCulture, Strings.Island_BatteryFormat, snap.MeterPercent),
            IslandPillFace.Clip or IslandPillFace.Drop => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_Clipboard : snap.Title,
            IslandPillFace.Bluetooth => string.IsNullOrWhiteSpace(snap.Title) ? "Bluetooth" : snap.Title,
            IslandPillFace.Camera => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_Camera : snap.Title,
            _ => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_Clipboard : snap.Title,
        };
    }

    /// <summary>
    /// Last-clip preview parks the pill in its own window and drops only that HWND
    /// from capture. The top bar stays in the shot. A plain "Clipboard" label, music, and
    /// meters stay in the bar.
    /// </summary>
    private void ApplyCaptureAffinity(IslandPresenceView snap, bool pillShown)
    {
        bool park = PillCapture.ShouldPark(
            snap.Face,
            pillShown,
            snap.Title,
            IslandNormalizer.IsLastClipIdle(Island?.Config.IdleLabel));
        if (park)
        {
            ParkPill();
            WindowCapture.SetExcludedFromCapture(_source?.Handle ?? IntPtr.Zero, false);
            return;
        }

        ReturnPill();
        if (_captureExcluded)
        {
            _captureExcluded = false;
            WindowCapture.SetExcludedFromCapture(_source?.Handle ?? IntPtr.Zero, false);
        }
    }

    private void ParkPill()
    {
        if (_pillParked)
        {
            Dispatcher.BeginInvoke(PlaceShot, DispatcherPriority.Render);
            return;
        }

        if (IslandPill.Parent is not Panel home)
        {
            return;
        }

        _pillHome = home;
        _pillHomeIndex = home.Children.IndexOf(IslandPill);
        _pillMargin = IslandPill.Margin;
        _pillAlignH = IslandPill.HorizontalAlignment;
        _pillAlignV = IslandPill.VerticalAlignment;
        var slot = new Border
        {
            Width = IslandPill.ActualWidth,
            Height = Math.Max(IslandPill.ActualHeight, 1),
            Margin = _pillMargin,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Background = Brushes.Transparent,
        };
        home.Children.Remove(IslandPill);
        home.Children.Insert(_pillHomeIndex, slot);
        _pillSlot = slot;

        IslandPill.Margin = new Thickness(0);
        IslandPill.HorizontalAlignment = HorizontalAlignment.Center;
        IslandPill.VerticalAlignment = VerticalAlignment.Center;
        _shot ??= new IslandShotWindow(this);
        _shot.Host.Children.Add(IslandPill);
        _pillParked = true;
        _captureExcluded = false;
        IslandPill.SizeChanged += OnParkedPillSize;
        Dispatcher.BeginInvoke(PlaceShot, DispatcherPriority.Render);
    }

    private void OnParkedPillSize(object sender, SizeChangedEventArgs e)
    {
        if (_pillSlot is not null)
        {
            _pillSlot.Width = IslandPill.ActualWidth;
            _pillSlot.Height = Math.Max(IslandPill.ActualHeight, 1);
        }

        Dispatcher.BeginInvoke(PlaceShot, DispatcherPriority.Render);
    }

    private void PlaceShot()
    {
        if (!_pillParked || _shot is null || _pillSlot is null || _source is null)
        {
            return;
        }

        Point origin;
        try
        {
            origin = _pillSlot.PointToScreen(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        uint dpi = Display.GetDpi(_source.Handle);
        int width = Display.DipToPixels(Math.Max(IslandPill.ActualWidth, 1), dpi);
        int height = Display.DipToPixels(Math.Max(IslandPill.ActualHeight, 1), dpi);
        _shot.Place((int)Math.Round(origin.X), (int)Math.Round(origin.Y), width, height);
    }

    private void ReturnPill()
    {
        if (!_pillParked)
        {
            return;
        }

        IslandPill.SizeChanged -= OnParkedPillSize;
        _shot?.Host.Children.Remove(IslandPill);
        IslandPill.Margin = _pillMargin;
        IslandPill.HorizontalAlignment = _pillAlignH;
        IslandPill.VerticalAlignment = _pillAlignV;
        if (_pillHome is not null)
        {
            if (_pillSlot is not null)
            {
                _pillHome.Children.Remove(_pillSlot);
            }

            if (!ReferenceEquals(IslandPill.Parent, _pillHome))
            {
                int index = Math.Clamp(_pillHomeIndex, 0, _pillHome.Children.Count);
                _pillHome.Children.Insert(index, IslandPill);
            }
        }

        _pillSlot = null;
        _pillParked = false;
        _shot?.Conceal();
    }

    private void ClearCaptureAffinity()
    {
        ReturnPill();
        _captureExcluded = false;
        WindowCapture.SetExcludedFromCapture(_source?.Handle ?? IntPtr.Zero, false);
    }
}
