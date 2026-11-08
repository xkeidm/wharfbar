using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WharfBar.Resources;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// All of the island's pure logic: the accept rule, dedupe, pruning, search matching, preview
/// formatting, and dropped-file name generation. Win32 and the file system are not here —
/// which is why all of it is unit-testable. The live services (<c>IslandService</c>,
/// <c>IslandStore</c>) only call into it.
/// </summary>
internal static class IslandPruner
{
    /// <summary>Clipboard content larger than this is not saved.</summary>
    internal const int MaxTextBytes = 64 * 1024;

    /// <summary>Number of characters shown in the pill and list preview.</summary>
    internal const int PreviewChars = 40;

    /// <summary>Length limit for the source app name in the list.</summary>
    internal const int SourceAppMaxChars = 24;

    /// <summary>
    /// Whether a captured clipboard item is taken into history.
    /// </summary>
    /// <param name="historyOptOut">
    /// <c>true</c> if the <c>CanIncludeInClipboardHistory</c> format is 0 — password managers
    /// keep their copies out of history with this flag, and we keep them out too. The caller must
    /// also pass <c>true</c> when it <b>could not save the format at all</b> (fail-closed): taking
    /// the content when the opt-out cannot be read would override the password manager's request.
    /// </param>
    /// <param name="text">The text that was read.</param>
    /// <param name="byteCount">UTF-8 byte length of the text.</param>
    internal static bool ShouldCapture(bool historyOptOut, string? text, int byteCount)
    {
        if (historyOptOut || text is null)
        {
            return false;
        }

        if (byteCount > MaxTextBytes || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Prepends the new entry. <paramref name="current"/> is expected to be sorted with the
    /// newest first. If the same text was copied consecutively, no duplicate is created; the
    /// existing entry is only moved to the front (the behavior of Windows history). Pruning is
    /// applied afterwards.
    /// </summary>
    internal static IReadOnlyList<IslandEntry> Prepend(
        IReadOnlyList<IslandEntry> current,
        IslandEntry entry,
        int maxItems,
        DateTimeOffset now,
        int retentionDays,
        bool persistent)
    {
        var result = new List<IslandEntry>(current.Count + 1);
        result.Add(entry);

        bool mergedWithFirst = current.Count > 0 && IsSameContent(current[0], entry);
        for (int i = mergedWithFirst ? 1 : 0; i < current.Count; i++)
        {
            result.Add(current[i]);
        }

        return Prune(result, maxItems, now, retentionDays, persistent);
    }

    /// <summary>
    /// Applies the day limit + entry-count ceiling. <paramref name="entries"/> is expected to be
    /// sorted with the newest first: <c>Take</c> cuts from the front, so the order is preserved and
    /// the entries are not re-sorted. If <paramref name="persistent"/> is false the day limit is
    /// ignored (in session-only mode older entries are kept as well).
    /// </summary>
    /// <remarks>
    /// <paramref name="retentionDays"/> of 0 (or less) does <b>not</b> mean "unlimited":
    /// the settings path (<c>IslandNormalizer.ClampRetentionDays</c>) clamps the value to 1..3650,
    /// so the <c>&gt; 0</c> branch is always taken; <c>&lt;= 0</c> applies only to tests that call
    /// this method directly and does not apply the day limit. An entry exactly at the boundary
    /// (<c>&gt;=</c> comparison) <b>stays</b>. If <paramref name="maxItems"/> is 0 or
    /// less the result is empty — the ceiling is not raised to 1.
    /// </remarks>
    internal static IReadOnlyList<IslandEntry> Prune(
        IReadOnlyList<IslandEntry> entries,
        int maxItems,
        DateTimeOffset now,
        int retentionDays,
        bool persistent)
    {
        IEnumerable<IslandEntry> kept = entries;

        if (persistent && retentionDays > 0)
        {
            DateTime cutoff = now.UtcDateTime.AddDays(-retentionDays);
            kept = kept.Where(e => e.CapturedAtUtc >= cutoff);
        }

        return kept.Take(Math.Max(0, maxItems)).ToList();
    }

    /// <summary>Whether the entry matches the search query. An empty query matches everything.</summary>
    internal static bool Matches(IslandEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string q = query.Trim();
        return Contains(entry.Text, q)
            || Contains(entry.FilePath, q)
            || Contains(entry.SourceApp, q);
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Collapses line breaks and extra whitespace into a single line and truncates long text.</summary>
    internal static string FormatPreview(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        string single = sb.ToString();
        if (single.Length <= PreviewChars)
        {
            return single;
        }

        // The cut is made on a UTF-16 unit; if the 40th character is a high surrogate (emoji etc.)
        // half of the pair remains and an empty box shows in the preview. The unit is stepped back.
        int cut = char.IsHighSurrogate(single[PreviewChars - 1]) ? PreviewChars - 1 : PreviewChars;
        return string.Concat(single.AsSpan(0, cut), "…");
    }

    /// <summary>Relative time label shown in the list.</summary>
    internal static string FormatRelativeTime(DateTime capturedAtUtc, DateTimeOffset now)
    {
        TimeSpan delta = now.UtcDateTime - capturedAtUtc;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 1)
        {
            return Strings.Island_RelativeNow;
        }

        if (delta.TotalHours < 1)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.Island_RelativeMinutes, (int)delta.TotalMinutes);
        }

        if (delta.TotalDays < 1)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.Island_RelativeHours, (int)delta.TotalHours);
        }

        return string.Format(CultureInfo.CurrentCulture, Strings.Island_RelativeDays, (int)delta.TotalDays);
    }

    /// <summary>
    /// Generates a file name for a name collision. The rule is applied to the <b>file name only</b>:
    /// first the path component is dropped with <c>Path.GetFileName</c>, then if <paramref name="suffix"/>
    /// is 0 the name is returned as is, otherwise " (n)" is inserted before the extension (including
    /// names without an extension). The returned value is always a bare file name — the path is built
    /// by the caller with <c>Path.Combine(DropRoot, result)</c>. That way a directory name containing
    /// a dot (<c>my.dir</c>) cannot break the path shape.
    /// </summary>
    internal static string BuildDropFileName(string fileName, int suffix)
    {
        string name = Path.GetFileName(fileName);
        if (suffix <= 0)
        {
            return name;
        }

        string marker = string.Format(CultureInfo.InvariantCulture, " ({0})", suffix);
        int dot = name.LastIndexOf('.');
        return dot > 0
            ? string.Concat(name.AsSpan(0, dot), marker, name.AsSpan(dot))
            : string.Concat(name, marker);
    }

    private static bool IsSameContent(IslandEntry a, IslandEntry b) =>
        a.Kind == b.Kind
        && string.Equals(a.Text, b.Text, StringComparison.Ordinal)
        && string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
}
