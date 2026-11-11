using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WharfBar.Services;
using WharfBar.Resources;

namespace WharfBar.Views;

/// <summary>
/// Clock click panel: recent notification text and a local month calendar.
/// One popup, no nested popup. The bell's Win+N path stays on the bar.
/// </summary>
public partial class ClockPanelFlyout : UserControl
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    private DateTime _today = DateTime.Now;
    private DateTime _month;
    private int _notificationLoad;

    public ClockPanelFlyout()
    {
        InitializeComponent();
    }

    /// <summary>Opens the system notification center (Win+N). The panel does not scrape that window.</summary>
    internal event EventHandler? OpenCenterRequested;

    internal void OnFlyoutOpened()
    {
        FlyoutRoot.Background = FlyoutSurface.Create();
        _today = DateTime.Now;
        ShowMonth(new DateTime(_today.Year, _today.Month, 1));
        _ = LoadNotificationsAsync();
    }

    private void OnOpenCenterClick(object sender, RoutedEventArgs e)
        => OpenCenterRequested?.Invoke(this, EventArgs.Empty);

    private void OnPreviousMonthClick(object sender, RoutedEventArgs e)
        => ShowMonth(_month.AddMonths(-1));

    private void OnNextMonthClick(object sender, RoutedEventArgs e)
        => ShowMonth(_month.AddMonths(1));

    private void ShowMonth(DateTime month)
    {
        _month = new DateTime(month.Year, month.Month, 1);
        MonthTitle.Text = _month.ToString("MMMM yyyy", Culture);
        CalendarDays.Children.Clear();

        string[] names = Culture.DateTimeFormat.AbbreviatedDayNames;
        DayOfWeek first = Culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            int index = ((int)first + i) % 7;
            CalendarDays.Children.Add(DayHeader(names[index]));
        }

        int lead = ((int)_month.DayOfWeek - (int)first + 7) % 7;
        for (int i = 0; i < lead; i++)
        {
            CalendarDays.Children.Add(new Border { Height = 28 });
        }

        int days = DateTime.DaysInMonth(_month.Year, _month.Month);
        for (int day = 1; day <= days; day++)
        {
            bool today = _today.Year == _month.Year && _today.Month == _month.Month && _today.Day == day;
            CalendarDays.Children.Add(DayCell(day, today));
        }
    }

    private TextBlock DayHeader(string name)
    {
        return new TextBlock
        {
            Text = name,
            FontSize = 11,
            Opacity = 0.65,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    /// <summary>A day is not a button. Clicking it does not create an event.</summary>
    private Border DayCell(int day, bool today)
    {
        var label = new TextBlock
        {
            Text = day.ToString(Culture),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cell = new Border
        {
            Height = 28,
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = label,
        };
        if (today)
        {
            // Live references: the month is repainted on every open, but the accent pair must also
            // follow a theme/accent swap instead of caching the palette current at build time.
            cell.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        }

        return cell;
    }

    private async Task LoadNotificationsAsync()
    {
        int generation = ++_notificationLoad;
        NotificationList.Children.Clear();
        ShowNotificationMessage(Strings.Clock_ReadingNotifications);
        try
        {
            UserNotificationListener listener = UserNotificationListener.Current;
            UserNotificationListenerAccessStatus access = listener.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                access = await listener.RequestAccessAsync();
            }

            if (generation != _notificationLoad)
            {
                return;
            }

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                ShowNotificationMessage(Strings.Clock_AccessDenied);
                return;
            }

            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (generation != _notificationLoad)
            {
                return;
            }
            var rows = new List<NotificationText>();
            foreach (UserNotification notification in notifications)
            {
                if (TryReadToast(notification, out NotificationText text))
                {
                    rows.Add(text);
                }
            }

            IReadOnlyList<NotificationText> recent = NotificationDigest.TakeRecent(rows);
            if (recent.Count == 0)
            {
                ShowNotificationMessage(Strings.Clock_NoNotifications);
                return;
            }

            NotificationStatus.Visibility = Visibility.Collapsed;
            foreach (NotificationText item in recent)
            {
                NotificationList.Children.Add(NotificationRow(item));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WharfBar] notifications failed: {ex.Message}");
            if (generation == _notificationLoad)
            {
                ShowNotificationMessage(Strings.Clock_ReadFailed);
            }
        }
    }

    private static bool TryReadToast(UserNotification notification, out NotificationText text)
    {
        text = default;
        try
        {
            foreach (NotificationBinding binding in notification.Notification.Visual.Bindings)
            {
                var parts = binding.GetTextElements();
                if (parts.Count == 0)
                {
                    continue;
                }

                string title = parts[0].Text ?? string.Empty;
                string body = parts.Count > 1
                    ? string.Join(" ", parts.Skip(1).Select(part => part.Text).Where(part => !string.IsNullOrWhiteSpace(part)))
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                text = new NotificationText(title.Trim(), body.Trim());
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WharfBar] notification text skipped: {ex.Message}");
        }

        return false;
    }

    private void ShowNotificationMessage(string message)
    {
        NotificationList.Children.Clear();
        NotificationStatus.Text = message;
        NotificationStatus.Visibility = Visibility.Visible;
    }

    private static StackPanel NotificationRow(NotificationText item)
    {
        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var body = new TextBlock
        {
            Text = item.Body,
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = string.IsNullOrWhiteSpace(item.Body) ? Visibility.Collapsed : Visibility.Visible,
        };
        return new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 10),
            Children = { title, body },
        };
    }
}
