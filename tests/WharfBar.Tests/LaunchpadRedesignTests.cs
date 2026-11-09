using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Regression tests for the launchpad redesign: the flat button template must render its
/// content, search must put results first, recent noise must be filtered, and the sizing /
/// keyboard helpers must stay inside the screen.
/// </summary>
public class LaunchpadRedesignTests
{
    [Fact]
    public void FlatTemplate_RendersTileContent()
    {
        RunSta(() =>
        {
            LaunchpadSession session = Session(
                [new LaunchpadApp("Edge", @"C:\apps\edge.lnk")],
                [new LaunchpadApp("Edge", @"C:\apps\edge.lnk")]);
            UIElement face = LaunchpadWin11Face.Create(session);
            Button tile = FindButtons(face).First(button => Equals(button.ToolTip, "Edge"));

            tile.ApplyTemplate();
            Assert.NotNull(tile.Template?.FindName("HoverOverlay", tile));

            ContentPresenter? presenter = FindVisual<ContentPresenter>(tile).FirstOrDefault();
            Assert.NotNull(presenter);
            Assert.Same(tile.Content, presenter.Content);
        });
    }

    [Fact]
    public void Search_ShowsResultsBeforePinnedAndRecent()
    {
        RunSta(() =>
        {
            LaunchpadSession session = Session(
                [new LaunchpadApp("Uygulama 01", @"C:\app1.lnk"), new LaunchpadApp("Edge", @"C:\edge.lnk")],
                [new LaunchpadApp("Uygulama 01", @"C:\app1.lnk")],
                recent: [new LaunchpadRecentItem("notlar.txt", @"C:\recent\notlar.lnk", DateTime.Now)]);
            UIElement face = LaunchpadWin11Face.Create(session);

            Find<TextBox>(face).Single().Text = "uygu";

            List<string> texts = Texts(face);
            int results = texts.FindIndex(text => text.StartsWith(Strings.Launchpad_Results, StringComparison.Ordinal));
            Assert.True(results >= 0, "results header was not rendered");
            Assert.Contains("Uygulama 01", texts);
            Assert.DoesNotContain(Strings.Launchpad_Pinned, texts);
            Assert.DoesNotContain(Strings.Launchpad_Recent, texts);
        });
    }

    [Fact]
    public void Search_HidesProtocolRecentNoise()
    {
        RunSta(() =>
        {
            LaunchpadSession session = Session(
                [new LaunchpadApp("Edge", @"C:\edge.lnk")],
                [],
                recent:
                [
                    new LaunchpadRecentItem("ms-screenclip:--source=HotKey (54)", @"C:\recent\a.lnk", DateTime.Now),
                    new LaunchpadRecentItem("gercek dosya.txt", @"C:\recent\b.lnk", DateTime.Now),
                ]);
            UIElement face = LaunchpadWin11Face.Create(session);

            Find<TextBox>(face).Single().Text = "screenclip";

            List<string> texts = Texts(face);
            Assert.DoesNotContain(texts, text => text.Contains("screenclip", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(Strings.Launchpad_NoMatches, texts);
        });
    }

    [Fact]
    public void Recent_ShowAllExpandsAndShowLessCollapses()
    {
        RunSta(() =>
        {
            var recent = new List<LaunchpadRecentItem>();
            for (int i = 1; i <= 6; i++)
            {
                recent.Add(new LaunchpadRecentItem($"dosya{i}.txt", $@"C:\recent\d{i}.lnk", DateTime.Now.AddMinutes(-i)));
            }

            LaunchpadSession session = Session([new LaunchpadApp("Edge", @"C:\edge.lnk")], [], recent: recent);
            UIElement face = LaunchpadWin11Face.Create(session);

            Assert.DoesNotContain("dosya5.txt", Texts(face));
            ClickButton(face, Strings.Launchpad_ShowAll);
            Assert.Contains("dosya5.txt", Texts(face));
            ClickButton(face, Strings.Launchpad_ShowLess);
            Assert.DoesNotContain("dosya5.txt", Texts(face));
        });
    }

    [Fact]
    public void Search_RanksExactAndPrefixBeforeSubstring()
    {
        IReadOnlyList<LaunchpadApp> ranked = LaunchpadCatalog.Rank(
            [
                new LaunchpadApp("Microsoft Edge", @"C:\edge.lnk"),
                new LaunchpadApp("Edge", @"C:\edge2.lnk"),
                new LaunchpadApp("Knowledge Edge", @"C:\edge3.lnk"),
            ],
            "edge");

        Assert.Equal(["Edge", "Microsoft Edge", "Knowledge Edge"], ranked.Select(app => app.Name).ToArray());
        Assert.Equal(0, LaunchpadCatalog.MatchRank("Edge", "edge"));
        Assert.Equal(1, LaunchpadCatalog.MatchRank("Edge Dev", "edge"));
        Assert.Equal(2, LaunchpadCatalog.MatchRank("Microsoft Edge", "edge"));
        Assert.Equal(3, LaunchpadCatalog.MatchRank("Knowledge Edge", "edge"));
        Assert.Equal(int.MaxValue, LaunchpadCatalog.MatchRank("Notepad", "edge"));
    }

    [Fact]
    public void Catalog_ScanFindsUrlShortcuts()
    {
        string root = Path.Combine(Path.GetTempPath(), "wharfbar-urlscan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Steam Game.lnk"), "a");
            File.WriteAllText(Path.Combine(root, "Web App.url"), "b");

            IReadOnlyList<LaunchpadApp> apps = LaunchpadCatalog.Scan(root);

            Assert.Equal(["Steam Game", "Web App"], apps.Select(app => app.Name).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Search_FoldsTurkishDottedAndDotlessI()
    {
        Assert.True(LaunchpadSearch.Contains("İnstagram", "instagram"));
        Assert.True(LaunchpadSearch.Contains("INTERNET", "internet"));
        Assert.True(LaunchpadSearch.Contains("Instagram", "ı"));
        Assert.True(LaunchpadSearch.Contains("Instagram", "INSTAGRAM"));
        Assert.False(LaunchpadSearch.Contains("Edge", "firefox"));
    }

    [Fact]
    public void Catalog_ScanSkipsStartupFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "wharfbar-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Programs", "Startup"));
        try
        {
            File.WriteAllText(Path.Combine(root, "Programs", "Startup", "WharfBar.lnk"), "a");
            File.WriteAllText(Path.Combine(root, "Alpha.lnk"), "b");

            IReadOnlyList<LaunchpadApp> apps = LaunchpadCatalog.Scan(root);

            Assert.Equal(["Alpha"], apps.Select(app => app.Name).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(1920, 1080, 680, 740, true)]
    [InlineData(910, 512, 680, 416, false)]
    [InlineData(800, 600, 680, 504, false)]
    [InlineData(640, 480, 560, 384, false)]
    public void Layout_FitsCardAndPhoneIntoTheScreen(
        double width,
        double height,
        double expectedCardWidth,
        double expectedCardHeight,
        bool expectedPhone)
    {
        double cardWidth = LaunchpadLayout.CardWidth(width);
        double cardHeight = LaunchpadLayout.CardHeight(height);

        Assert.Equal(expectedCardWidth, cardWidth);
        Assert.Equal(expectedCardHeight, cardHeight);
        Assert.Equal(expectedPhone, LaunchpadLayout.PhoneFits(width, cardWidth));
        Assert.True(LaunchpadLayout.ScrollHeight(cardHeight) >= 160);
    }

    [Fact]
    public void Nav_MovesThroughRowsAndSections()
    {
        var sections = new List<LaunchpadNavSection>
        {
            new(0, 3, 3),
            new(3, 8, 4),
        };

        Assert.Equal(1, LaunchpadTileNav.Move(sections, 0, LaunchpadMove.Right));
        Assert.Equal(0, LaunchpadTileNav.Move(sections, 0, LaunchpadMove.Left));
        Assert.Equal(3, LaunchpadTileNav.Move(sections, 1, LaunchpadMove.Down));
        Assert.Equal(4, LaunchpadTileNav.Move(sections, 3, LaunchpadMove.Right));
        Assert.Equal(7, LaunchpadTileNav.Move(sections, 7, LaunchpadMove.Left));
        Assert.Equal(3, LaunchpadTileNav.Move(sections, 2, LaunchpadMove.Down));
        Assert.Equal(2, LaunchpadTileNav.Move(sections, 5, LaunchpadMove.Up));
        Assert.Equal(6, LaunchpadTileNav.Move(sections, 5, LaunchpadMove.Right));
        Assert.Equal(6, LaunchpadTileNav.Move(sections, 6, LaunchpadMove.Right));
        Assert.Equal(-1, LaunchpadTileNav.Move(sections, 1, LaunchpadMove.Up));
        Assert.Equal(0, LaunchpadTileNav.Move(sections, -1, LaunchpadMove.Down));
    }

    [Fact]
    public void Recent_CleanDropsNoiseAndLaunchCounters()
    {
        var items = new List<LaunchpadRecentItem>
        {
            new("ms-screenclip:--source=HotKey (54)", @"C:\a.lnk", DateTime.Now),
            new("report.pdf (3)", @"C:\b.lnk", DateTime.Now.AddMinutes(-1)),
            new("report.pdf (2)", @"C:\c.lnk", DateTime.Now.AddMinutes(-2)),
            new("notes.txt", @"C:\d.lnk", DateTime.Now.AddMinutes(-3)),
            new("notes.txt", @"C:\e.lnk", DateTime.Now.AddMinutes(-4)),
        };

        IReadOnlyList<LaunchpadRecentItem> clean = LaunchpadRecent.Clean(items);

        Assert.Equal(["report.pdf", "notes.txt"], clean.Select(item => item.Name).ToArray());
        Assert.Equal(@"C:\b.lnk", clean[0].Path);
        Assert.Equal("report.pdf", LaunchpadRecent.DisplayName("report.pdf (3)"));
        Assert.Equal("report (final).pdf", LaunchpadRecent.DisplayName("report (final).pdf"));
        Assert.False(LaunchpadRecent.IsNoise("notes.txt"));
    }

    // ---------------------------------------------------------------- helpers

    private static LaunchpadSession Session(
        IReadOnlyList<LaunchpadApp> apps,
        IReadOnlyList<LaunchpadApp>? pinned = null,
        IReadOnlyList<LaunchpadRecentItem>? recent = null)
    {
        return new LaunchpadSession
        {
            Config = new AppConfig(),
            Apps = apps,
            Icon = _ => new DrawingImage(),
            Open = _ => { },
            Close = () => { },
            UseStyle = _ => { },
            OpenWindowsSettings = () => { },
            OpenWharfSettings = () => { },
            OpenWidgets = () => { },
            OpenPlugins = () => { },
            OpenPath = _ => { },
            Pinned = pinned ?? [],
            Recent = recent ?? [],
        };
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null)
        {
            throw error;
        }
    }

    private static void ClickButton(DependencyObject root, string content)
    {
        Button? button = FindButtons(root).FirstOrDefault(candidate => Equals(candidate.Content, content));
        Assert.NotNull(button);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static List<string> Texts(DependencyObject root)
    {
        var texts = new List<string>();
        Walk(root, node =>
        {
            if (node is TextBlock block && block.Text.Length > 0)
            {
                texts.Add(block.Text);
            }

            if (node is ContentControl control && control.Content is string label && label.Length > 0)
            {
                texts.Add(label);
            }
        });
        return texts;
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        var buttons = new List<Button>();
        Walk(root, node =>
        {
            if (node is Button button)
            {
                buttons.Add(button);
            }
        });
        return buttons;
    }

    private static IEnumerable<T> Find<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new List<T>();
        Walk(root, node =>
        {
            if (node is T match)
            {
                found.Add(match);
            }
        });
        return found;
    }

    private static IEnumerable<T> FindVisual<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisual<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Walk(DependencyObject node, Action<DependencyObject> visit)
    {
        visit(node);
        foreach (object child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is DependencyObject next)
            {
                Walk(next, visit);
            }
        }
    }
}
