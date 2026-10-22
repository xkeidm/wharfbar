namespace WharfBar.Services;

internal static class WidgetMetrics
{
    internal const double GripDip = 28;
    internal const double ClockWidth = 240;
    internal const double ClockHeight = 132;
    internal const double CalendarWidth = 300;
    internal const double CalendarHeight = 268;
    internal const double Gap = 8;
    internal const double Margin = 24;
    internal const double StackGap = 12;

    internal static DipSize SizeOf(string id) =>
        id == PluginCatalog.Calendar
            ? new DipSize(CalendarWidth, CalendarHeight)
            : new DipSize(ClockWidth, ClockHeight);
}
