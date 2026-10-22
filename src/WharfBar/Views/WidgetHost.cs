using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views;

internal static class WidgetHost
{
    private static readonly Dictionary<string, DesktopWidgetWindow> Open = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static ConfigService? _service;
    private static HwndSource? _messages;
    private static uint _taskbarCreated;
    private static DispatcherTimer? _retry;
    private static int _retries;
    private static DispatcherTimer? _poll;
    private static Thread? _reader;
    private static bool _readQueued;
    private static IReadOnlyList<DipRect> _icons = [];
    private static System.Windows.Automation.AutomationElement? _iconWatchRoot;
    private static System.Windows.Automation.StructureChangedEventHandler? _iconStructureChanged;

    internal static void Attach(ConfigService service)
    {
        _service = service;
        EnsureReader();
        EnsureMessages();
    }

    internal static void Apply()
    {
        if (_service is null)
        {
            return;
        }

        List<string> open = _service.Current.Island.OpenWidgets ?? [];
        Sync(PluginCatalog.Clock, open.Contains(PluginCatalog.Clock), ClockBody());
        Sync(PluginCatalog.Calendar, open.Contains(PluginCatalog.Calendar), CalendarBody());
        PlaceAll(saveWhenMoved: false);
        QueueIconRead();
    }

    private static void Sync(string id, bool wanted, UIElement body)
    {
        Open.TryGetValue(id, out DesktopWidgetWindow? window);
        if (!wanted)
        {
            window?.Close();
            Open.Remove(id);
            return;
        }

        if (window is not null)
        {
            return;
        }

        DipSize size = WidgetMetrics.SizeOf(id);
        string title = id == PluginCatalog.Calendar
            ? Strings.Gallery_CardCalendarName
            : Strings.Gallery_CardClockName;
        window = new DesktopWidgetWindow(title, size.Width, size.Height, body)
        {
            Visibility = Visibility.Hidden,
        };
        string captured = id;
        window.CloseRequested += () => CloseWidget(captured);
        window.DragFinished += () => FinishDrag(captured);
        window.Closed += (_, _) => Open.Remove(captured);
        Open[id] = window;
        window.EnsureHwnd();
    }

    private static UIElement ClockBody()
    {
        var time = new TextBlock { FontSize = 42, HorizontalAlignment = HorizontalAlignment.Center };
        var date = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(time);
        stack.Children.Add(date);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Tick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            time.Text = now.ToString("HH:mm", CultureInfo.CurrentUICulture);
            date.Text = now.ToString("d", CultureInfo.CurrentUICulture);
        }
        Tick(null, EventArgs.Empty);
        timer.Tick += Tick;
        stack.Loaded += (_, _) => timer.Start();
        stack.Unloaded += (_, _) => timer.Stop();
        return stack;
    }

    private static UIElement CalendarBody() =>
        new System.Windows.Controls.Calendar { Margin = new Thickness(8) };

    private static void PlaceAll(bool saveWhenMoved)
    {
        if (_service is null)
        {
            return;
        }

        IntPtr host = DesktopIconHost.TryGet();
        if (host == IntPtr.Zero)
        {
            HideAndRetry();
            return;
        }

        StopRetry();
        IReadOnlyList<WidgetPlacement> saved = _service.Current.Island.WidgetPlacements ?? [];
        DipRect? clock = null;
        if (Open.ContainsKey(PluginCatalog.Clock))
        {
            clock = PlaceOne(PluginCatalog.Clock, host, saved, other: null, fallback: null, saveWhenMoved);
        }

        if (Open.ContainsKey(PluginCatalog.Calendar))
        {
            DipPoint? under = clock is DipRect snapped
                ? WidgetDefaults.Under(new DipPoint(snapped.X, snapped.Y), WidgetMetrics.SizeOf(PluginCatalog.Clock))
                : null;
            PlaceOne(PluginCatalog.Calendar, host, saved, clock, under, saveWhenMoved);
        }
    }

    private static DipRect? PlaceOne(
        string id,
        IntPtr host,
        IReadOnlyList<WidgetPlacement> saved,
        DipRect? other,
        DipPoint? fallback,
        bool saveWhenMoved)
    {
        if (!Open.TryGetValue(id, out DesktopWidgetWindow? window))
        {
            return null;
        }

        DipSize size = WidgetMetrics.SizeOf(id);
        DipPoint desired = fallback ?? WidgetDefaults.Clock(PrimaryWork());
        foreach (WidgetPlacement placement in saved)
        {
            if (placement.Id == id)
            {
                desired = new DipPoint(placement.X, placement.Y);
                break;
            }
        }

        DipRect work = ReadWork(NativeMethods.MonitorFromWindow(
            window.Hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST));
        IReadOnlyList<DipRect> obstacles = WidgetSnap.Obstacles(_icons, other);
        DipPoint snapped = WidgetSnap.Snap(desired, size, work, obstacles);
        ParentAndMove(window, host, snapped, size);
        if (saveWhenMoved && (snapped.X != desired.X || snapped.Y != desired.Y))
        {
            SavePoint(id, snapped);
        }

        return new DipRect(snapped.X, snapped.Y, size.Width, size.Height);
    }

    private static void ParentAndMove(DesktopWidgetWindow window, IntPtr host, DipPoint dip, DipSize size)
    {
        IntPtr hwnd = window.Hwnd;
        if (NativeMethods.SetParent(hwnd, host) == IntPtr.Zero)
        {
            Debug.WriteLine($"WharfBar widget SetParent failed: win32error={Marshal.GetLastWin32Error()}");
            window.Visibility = Visibility.Hidden;
            HideAndRetry();
            return;
        }

        double centerXDip = dip.X + size.Width / 2d;
        double centerYDip = dip.Y + size.Height / 2d;
        int probeX = WidgetScale.ToPhysical(centerXDip, 96);
        int probeY = WidgetScale.ToPhysical(centerYDip, 96);
        var probeRect = new RECT
        {
            Left = probeX,
            Top = probeY,
            Right = probeX + 1,
            Bottom = probeY + 1,
        };
        IntPtr placementMonitor = NativeMethods.MonitorFromRect(
            ref probeRect,
            NativeMethods.MONITOR_DEFAULTTONEAREST);
        uint dpi = 96;
        if (placementMonitor != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(
                placementMonitor,
                NativeMethods.MDT_EFFECTIVE_DPI,
                out uint monitorDpi,
                out _) == 0
            && monitorDpi != 0)
        {
            dpi = monitorDpi;
        }

        var screen = new POINT
        {
            X = WidgetScale.ToPhysical(dip.X, dpi),
            Y = WidgetScale.ToPhysical(dip.Y, dpi),
        };
        var client = screen;
        if (!NativeMethods.ScreenToClient(host, ref client))
        {
            Debug.WriteLine($"WharfBar widget ScreenToClient failed: win32error={Marshal.GetLastWin32Error()}");
            return;
        }

        int width = WidgetScale.ToPhysical(size.Width, dpi);
        int height = WidgetScale.ToPhysical(size.Height, dpi);
        _ = NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOP,
            client.X,
            client.Y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        window.Show();
        _ = NativeMethods.SetParent(hwnd, host);
        _ = NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOP,
            client.X,
            client.Y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        Dwm.SetSystemBackdrop(hwnd, DwmSystemBackdrop.None);
        Dwm.SetCornerPreference(hwnd, DwmCornerPreference.Round);
    }

    private static void FinishDrag(string id)
    {
        if (_service is null || !Open.TryGetValue(id, out DesktopWidgetWindow? window))
        {
            return;
        }

        if (!NativeMethods.GetWindowRect(window.Hwnd, out RECT rect))
        {
            return;
        }

        uint dpi = NativeMethods.GetDpiForWindow(window.Hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var desired = new DipPoint(WidgetScale.ToDip(rect.Left, dpi), WidgetScale.ToDip(rect.Top, dpi));
        DipRect? other = OtherRect(id);
        DipSize size = WidgetMetrics.SizeOf(id);
        IntPtr monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        DipPoint snapped = WidgetSnap.Snap(desired, size, ReadWork(monitor), WidgetSnap.Obstacles(_icons, other));
        IntPtr host = DesktopIconHost.TryGet();
        if (host != IntPtr.Zero)
        {
            ParentAndMove(window, host, snapped, size);
        }

        SavePoint(id, snapped);
    }

    private static DipRect? OtherRect(string id)
    {
        string otherId = id == PluginCatalog.Clock ? PluginCatalog.Calendar : PluginCatalog.Clock;
        if (!Open.TryGetValue(otherId, out DesktopWidgetWindow? other)
            || !NativeMethods.GetWindowRect(other.Hwnd, out RECT rect))
        {
            return null;
        }

        uint dpi = NativeMethods.GetDpiForWindow(other.Hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return new DipRect(
            WidgetScale.ToDip(rect.Left, dpi),
            WidgetScale.ToDip(rect.Top, dpi),
            WidgetScale.ToDip(rect.Width, dpi),
            WidgetScale.ToDip(rect.Height, dpi));
    }

    private static void SavePoint(string id, DipPoint point)
    {
        if (_service is null)
        {
            return;
        }

        AppConfig config = _service.Current;
        config.Island.WidgetPlacements = WidgetPlacementNormalizer.Upsert(
            config.Island.WidgetPlacements, id, point.X, point.Y);
        _service.Save(config);
    }

    private static void CloseWidget(string id)
    {
        if (_service is null)
        {
            return;
        }

        AppConfig config = _service.Current;
        if (PluginCatalog.SetEnabled(config, id, false))
        {
            _service.Save(config);
        }

        Apply();
    }

    private static void HideAndRetry()
    {
        foreach (DesktopWidgetWindow window in Open.Values)
        {
            window.Visibility = Visibility.Hidden;
        }

        if (_retry is not null)
        {
            return;
        }

        _retries = 0;
        _retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _retry.Tick += (_, _) =>
        {
            _retries++;
            if (DesktopIconHost.TryGet() != IntPtr.Zero || _retries >= 20)
            {
                StopRetry();
            }

            if (DesktopIconHost.TryGet() != IntPtr.Zero)
            {
                PlaceAll(saveWhenMoved: true);
            }
        };
        _retry.Start();
    }

    private static void StopRetry()
    {
        _retry?.Stop();
        _retry = null;
    }

    private static DipRect PrimaryWork()
    {
        var probe = new RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 };
        IntPtr monitor = NativeMethods.MonitorFromRect(ref probe, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        return ReadWork(monitor);
    }

    private static DipRect ReadWork(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return new DipRect(0, 0, 800, 600);
        }

        uint dpiX = 96;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint x, out _) == 0)
        {
            dpiX = x == 0 ? 96 : x;
        }

        return WidgetScale.WorkToDip(info.rcWork, dpiX);
    }

    private static void EnsureMessages()
    {
        if (_messages is not null)
        {
            return;
        }

        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _messages = new HwndSource(new HwndSourceParameters("WharfBar.WidgetHost")
        {
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        _messages.AddHook(OnMessage);
    }

    private static IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && msg == (int)_taskbarCreated)
        {
            Rebuild();
        }
        else if (msg == NativeMethods.WM_THEMECHANGED)
        {
            foreach (DesktopWidgetWindow window in Open.Values)
            {
                window.ApplyTheme();
            }

            QueueIconRead();
        }
        else if (msg == NativeMethods.WM_DISPLAYCHANGE)
        {
            PlaceAll(saveWhenMoved: true);
            QueueIconRead();
        }

        return IntPtr.Zero;
    }

    private static void Rebuild()
    {
        foreach (DesktopWidgetWindow window in Open.Values.ToList())
        {
            window.Close();
        }

        Open.Clear();
        Apply();
        TrySubscribeIconChanges();
    }

    private static void EnsureReader()
    {
        if (_reader is not null)
        {
            return;
        }

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "WharfBar.DesktopIcons" };
        _reader.SetApartmentState(ApartmentState.STA);
        _reader.Start();
        TrySubscribeIconChanges();
    }

    private static void ReadLoop()
    {
        while (true)
        {
            lock (Gate)
            {
                while (!_readQueued)
                {
                    Monitor.Wait(Gate);
                }

                _readQueued = false;
            }

            IReadOnlyList<DipRect> icons = DesktopIconRects.ReadInDip();
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _icons = icons;
                PlaceAll(saveWhenMoved: true);
            });
        }
    }

    private static void QueueIconRead()
    {
        lock (Gate)
        {
            _readQueued = true;
            Monitor.Pulse(Gate);
        }
    }

    private static void UnsubscribeIconChanges()
    {
        if (_iconWatchRoot is null || _iconStructureChanged is null)
        {
            return;
        }

        try
        {
            System.Windows.Automation.Automation.RemoveStructureChangedEventHandler(
                _iconWatchRoot,
                _iconStructureChanged);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icon unwatch failed: {ex.Message}");
        }

        _iconWatchRoot = null;
        _iconStructureChanged = null;
    }

    private static void TrySubscribeIconChanges()
    {
        UnsubscribeIconChanges();
        try
        {
            IntPtr host = DesktopIconHost.TryGet();
            IntPtr defView = NativeMethods.FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr list = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            if (list == IntPtr.Zero)
            {
                StartPoll();
                return;
            }

            var root = System.Windows.Automation.AutomationElement.FromHandle(list);
            _iconStructureChanged = (_, _) => QueueIconRead();
            System.Windows.Automation.Automation.AddStructureChangedEventHandler(
                root,
                System.Windows.Automation.TreeScope.Children,
                _iconStructureChanged);
            _iconWatchRoot = root;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icon watch failed: {ex.Message}");
            StartPoll();
        }
    }

    private static void StartPoll()
    {
        if (_poll is not null)
        {
            return;
        }

        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _poll.Tick += (_, _) =>
        {
            if (Open.Count > 0)
            {
                QueueIconRead();
            }
        };
        _poll.Start();
    }
}
