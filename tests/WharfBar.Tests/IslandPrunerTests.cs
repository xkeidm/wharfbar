using System;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandPrunerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;

    private static IslandEntry Text(string text, DateTime? at = null, string? app = null) =>
        new(1, IslandEntryKind.Text, text, null, at ?? NowUtc, app);

    private static IslandEntry File(string path, DateTime? at = null) =>
        new(2, IslandEntryKind.File, System.IO.Path.GetFileName(path), path, at ?? NowUtc, null);

    [Fact]
    public void ShouldCapture_Rejects_HistoryOptOut()
    {
        Assert.False(IslandPruner.ShouldCapture(historyOptOut: true, "hello", 5));
    }

    [Fact]
    public void ShouldCapture_Rejects_Empty_And_Whitespace()
    {
        Assert.False(IslandPruner.ShouldCapture(false, null, 0));
        Assert.False(IslandPruner.ShouldCapture(false, string.Empty, 0));
        Assert.False(IslandPruner.ShouldCapture(false, "   \t\n  ", 8));
    }

    [Fact]
    public void ShouldCapture_Rejects_Oversized()
    {
        Assert.False(IslandPruner.ShouldCapture(false, "x", IslandPruner.MaxTextBytes + 1));
    }

    [Fact]
    public void ShouldCapture_Accepts_Normal()
    {
        Assert.True(IslandPruner.ShouldCapture(false, "hello", 5));
        Assert.True(IslandPruner.ShouldCapture(false, "x", IslandPruner.MaxTextBytes));
    }

    [Fact]
    public void Prepend_Moves_Consecutive_Duplicate_ToTop_WithoutDuplicating()
    {
        IslandEntry a = Text("aaa", NowUtc.AddMinutes(-1));
        IslandEntry b = Text("bbb", NowUtc.AddMinutes(-5));
        IslandEntry again = Text("aaa", NowUtc);

        IReadOnlyList<IslandEntry> result = IslandPruner.Prepend(
            [a, b], again, 200, Now, 30, persistent: true);

        Assert.Equal(2, result.Count);
        Assert.Equal("aaa", result[0].Text);
        Assert.Equal(NowUtc, result[0].CapturedAtUtc);
        Assert.Equal("bbb", result[1].Text);
    }

    [Fact]
    public void Prepend_Accepts_Empty_Current()
    {
        IslandEntry only = Text("ilk", NowUtc);

        IReadOnlyList<IslandEntry> result = IslandPruner.Prepend(
            [], only, 200, Now, 30, persistent: true);

        Assert.Single(result);
        Assert.Equal(only, result[0]);
    }

    [Fact]
    public void Prepend_Keeps_NonConsecutive_Duplicates()
    {
        IslandEntry b = Text("other", NowUtc.AddMinutes(-5));
        IslandEntry a1 = Text("dup", NowUtc.AddMinutes(-9));
        IslandEntry a2 = Text("dup", NowUtc);

        IReadOnlyList<IslandEntry> result = IslandPruner.Prepend(
            [b, a1], a2, 200, Now, 30, persistent: true);

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Count(e => e.Text == "dup"));
    }

    [Fact]
    public void Prune_Drops_Entries_OlderThan_RetentionDays()
    {
        IslandEntry fresh = Text("fresh", NowUtc.AddHours(-1));
        IslandEntry old = Text("old", NowUtc.AddDays(-31));

        IReadOnlyList<IslandEntry> result = IslandPruner.Prune(
            [fresh, old], 200, Now, retentionDays: 30, persistent: true);

        Assert.Single(result);
        Assert.Equal("fresh", result[0].Text);
    }

    [Fact]
    public void Prune_Keeps_Entry_Exactly_At_RetentionBoundary()
    {
        IslandEntry boundary = Text("tam sınır", NowUtc.AddDays(-30));
        IslandEntry justPast = Text("bir saniye geç", NowUtc.AddDays(-30).AddSeconds(-1));

        IReadOnlyList<IslandEntry> result = IslandPruner.Prune(
            [boundary, justPast], 200, Now, retentionDays: 30, persistent: true);

        Assert.Single(result);
        Assert.Equal("tam sınır", result[0].Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Prune_Returns_Empty_When_MaxItems_Is_NotPositive(int maxItems)
    {
        var entries = new System.Collections.Generic.List<IslandEntry>
        {
            Text("a", NowUtc),
            Text("b", NowUtc.AddMinutes(-1)),
        };

        Assert.Empty(IslandPruner.Prune(entries, maxItems, Now, 30, persistent: true));
    }

    [Fact]
    public void Prune_Ignores_RetentionDays_WhenSession()
    {
        IslandEntry veryOld = Text("ancient", NowUtc.AddDays(-900));

        IReadOnlyList<IslandEntry> result = IslandPruner.Prune(
            [veryOld], 200, Now, retentionDays: 30, persistent: false);

        Assert.Single(result);
    }

    [Fact]
    public void Prune_Applies_MaxItems_HardCap()
    {
        var many = new System.Collections.Generic.List<IslandEntry>();
        for (int i = 0; i < 50; i++)
        {
            many.Add(Text($"e{i}", NowUtc.AddSeconds(-i)));
        }

        IReadOnlyList<IslandEntry> result = IslandPruner.Prune(many, 10, Now, 30, persistent: true);

        Assert.Equal(10, result.Count);
        Assert.Equal("e0", result[0].Text);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Hello", true)]
    [InlineData("hello", true)]
    [InlineData("ELL", true)]
    [InlineData("zzz", false)]
    public void Matches_IsCaseInsensitive_AndTreatsBlankAsMatchAll(string? query, bool expected)
    {
        Assert.Equal(expected, IslandPruner.Matches(Text("Hello"), query));
    }

    [Fact]
    public void Matches_Searches_FileName_And_SourceApp()
    {
        Assert.True(IslandPruner.Matches(File(@"C:\x\rapor.pdf"), "rapor"));
        Assert.True(IslandPruner.Matches(Text("kaynak", app: "Notepad"), "notepad"));
    }

    [Fact]
    public void FormatPreview_CollapsesWhitespace_AndTruncates()
    {
        Assert.Equal("tek satır", IslandPruner.FormatPreview("tek\r\nsatır"));
        Assert.Equal("a b c", IslandPruner.FormatPreview("  a   b \t c  "));

        string long_ = new('x', 100);
        string result = IslandPruner.FormatPreview(long_);
        Assert.Equal(IslandPruner.PreviewChars + 1, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void FormatPreview_ReturnsEmpty_ForEmptyText()
    {
        Assert.Equal(string.Empty, IslandPruner.FormatPreview(string.Empty));
    }

    [Fact]
    public void FormatPreview_KeepsShortTextIntact()
    {
        Assert.Equal("kısa", IslandPruner.FormatPreview("kısa"));
    }

    [Fact]
    public void FormatRelativeTime_Formats_MinutesHoursDays()
    {
        Assert.Equal("şimdi", IslandPruner.FormatRelativeTime(NowUtc, Now));
        Assert.Equal("3 dk önce", IslandPruner.FormatRelativeTime(NowUtc.AddMinutes(-3), Now));
        Assert.Equal("5 sa önce", IslandPruner.FormatRelativeTime(NowUtc.AddHours(-5), Now));
        Assert.Equal("2 gün önce", IslandPruner.FormatRelativeTime(NowUtc.AddDays(-2), Now));
    }

    [Fact]
    public void FormatRelativeTime_Clamps_FutureTimestamps_ToNow()
    {
        Assert.Equal("şimdi", IslandPruner.FormatRelativeTime(NowUtc.AddMinutes(10), Now));
        Assert.Equal("şimdi", IslandPruner.FormatRelativeTime(NowUtc.AddDays(3), Now));
    }

    [Theory]
    [InlineData("rapor.pdf", 0, "rapor.pdf")]
    [InlineData("rapor.pdf", 2, "rapor (2).pdf")]
    [InlineData("arsiv.tar.gz", 3, "arsiv.tar (3).gz")]
    [InlineData("noext", 1, "noext (1)")]
    [InlineData("my.folder", 1, "my (1).folder")]
    public void BuildDropFileName_AppendsSuffix_BeforeExtension(string input, int suffix, string expected)
    {
        Assert.Equal(expected, IslandPruner.BuildDropFileName(input, suffix));
    }

    [Theory]
    [InlineData(@"C:\my.dir\file", 1, "file (1)")]
    [InlineData(@"D:\a.b\c\rapor.pdf", 2, "rapor (2).pdf")]
    [InlineData(@"C:\my.dir\file", 0, "file")]
    public void BuildDropFileName_Uses_Only_FileName_Part_OfFullPath(
        string input, int suffix, string expected)
    {
        Assert.Equal(expected, IslandPruner.BuildDropFileName(input, suffix));
    }
}
