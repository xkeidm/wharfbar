using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using WharfBar.Native;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views;

internal sealed class DesktopWidgetWindow : Window
{
    private readonly Border _grip;
    private readonly TextBlock _title;
    private Point _dragOrigin;
    private bool _dragging;

    internal DesktopWidgetWindow(string title, double width, double height, UIElement body)
    {
        Title = title;
        Width = width;
        Height = height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        AllowsTransparency = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = Build(title, body);
        _grip = (Border)((DockPanel)Content).Children[0];
        _title = (TextBlock)((Grid)_grip.Child).Children[0];
        ApplyTheme();
    }

    internal IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    internal event Action? CloseRequested;

    internal event Action? DragFinished;

    internal void ApplyTheme()
    {
        bool dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        Color fill = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xFA, 0xFA, 0xFA);
        Color foreground = dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A);
        Background = new SolidColorBrush(fill);
        _grip.Background = new SolidColorBrush(dark ? Color.FromRgb(0x2A, 0x2A, 0x2A) : Color.FromRgb(0xEE, 0xEE, 0xEE));
        _title.Foreground = new SolidColorBrush(foreground);
    }

    internal void EnsureHwnd()
    {
        _ = new WindowInteropHelper(this).EnsureHandle();
        WindowOverlayStyles.ExcludeFromTaskSwitcher(Hwnd);
        Dwm.SetSystemBackdrop(Hwnd, DwmSystemBackdrop.None);
        Dwm.SetCornerPreference(Hwnd, DwmCornerPreference.Round);
    }

    private DockPanel Build(string title, UIElement body)
    {
        var grip = new Border { Height = WidgetMetrics.GripDip };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        var close = new Button
        {
            Content = Strings.Widget_Close,
            Width = 28,
            Height = 28,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };
        close.Click += (_, _) => CloseRequested?.Invoke();
        Grid.SetColumn(close, 1);
        grid.Children.Add(label);
        grid.Children.Add(close);
        grip.Child = grid;
        grip.MouseLeftButtonDown += OnGripDown;
        grip.MouseMove += OnGripMove;
        grip.MouseLeftButtonUp += OnGripUp;

        var panel = new DockPanel();
        DockPanel.SetDock(grip, Dock.Top);
        panel.Children.Add(grip);
        panel.Children.Add(body);
        return panel;
    }

    private void OnGripDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragOrigin = PointToScreen(e.GetPosition(this));
        _grip.CaptureMouse();
    }

    private void OnGripMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point now = PointToScreen(e.GetPosition(this));
        // The host repositions from the latest screen origin on DragFinished.
        // Live move is a screen delta applied by the host through MoveBy.
        MoveBy(now.X - _dragOrigin.X, now.Y - _dragOrigin.Y);
        _dragOrigin = now;
    }

    private void OnGripUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _grip.ReleaseMouseCapture();
        DragFinished?.Invoke();
    }

    internal void MoveBy(double dx, double dy)
    {
        IntPtr parent = NativeMethods.GetParent(Hwnd);
        if (parent == IntPtr.Zero || !NativeMethods.GetWindowRect(Hwnd, out RECT rect))
        {
            return;
        }

        var origin = new POINT { X = rect.Left + (int)Math.Round(dx), Y = rect.Top + (int)Math.Round(dy) };
        if (!NativeMethods.ScreenToClient(parent, ref origin))
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(
            Hwnd,
            NativeMethods.HWND_TOP,
            origin.X,
            origin.Y,
            rect.Width,
            rect.Height,
            NativeMethods.SWP_NOACTIVATE);
    }
}
