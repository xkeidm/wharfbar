namespace WharfBar.Services;

/// <summary>Title and body already read from a toast. No live listener.</summary>
internal readonly record struct NotificationText(string Title, string Body);

/// <summary>Caps the clock-panel list. The listener itself stays in the flyout.</summary>
internal static class NotificationDigest
{
    internal const int MaxItems = 8;

    internal static IReadOnlyList<NotificationText> TakeRecent(IReadOnlyList<NotificationText>? items)
    {
        if (items is null || items.Count == 0)
        {
            return [];
        }

        int count = Math.Min(MaxItems, items.Count);
        var copy = new NotificationText[count];
        for (int i = 0; i < count; i++)
        {
            copy[i] = items[i];
        }

        return copy;
    }
}
