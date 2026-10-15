using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Resources;

namespace WharfBar.Views;

/// <summary>Desktop clock and calendar. They are not island faces and not AppBars.</summary>
internal static class WidgetHost
{
    private static readonly Dictionary<string, Window> Open = new(StringComparer.Ordinal);

    internal static void Apply(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Island.OpenWidgets ??= [];
        Sync("clock", config.Island.OpenWidgets.Contains("clock"), () => Build(Strings.Gallery_CardClockName, ClockBody()));
        Sync("calendar", config.Island.OpenWidgets.Contains("calendar"), () => Build(Strings.Gallery_CardCalendarName, CalendarBody()));
    }

    private static void Sync(string id, bool wanted, Func<Window> create)
    {
        Open.TryGetValue(id, out Window? window);
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

        window = create();
        window.Closed += (_, _) => Open.Remove(id);
        Open[id] = window;
        window.Show();
    }

    private static Window Build(string title, UIElement body) => new()
    {
        Title = title,
        Width = 280,
        Height = 220,
        WindowStyle = WindowStyle.ToolWindow,
        ShowInTaskbar = false,
        Topmost = true,
        Content = body,
    };

    private static UIElement ClockBody()
    {
        var label = new TextBlock { FontSize = 42, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => label.Text = DateTime.Now.ToString("HH:mm");
        label.Text = DateTime.Now.ToString("HH:mm");
        label.Loaded += (_, _) => timer.Start();
        label.Unloaded += (_, _) => timer.Stop();
        return label;
    }

    private static UIElement CalendarBody()
    {
        var calendar = new System.Windows.Controls.Calendar { Margin = new Thickness(8) };
        return calendar;
    }
}
