using WharfBar.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Models;
using WharfBar.Services;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

public class LaunchpadFaceTests
{
    [Fact]
    public void Face_ListsAppsPinsAndRecent()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                AssertFaces();
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

    [Fact]
    public void DockGlyph_IsNotTheRecycleIcon()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var icons = new AppIconService();
                var launchpad = new DockAppItem(
                    "builtin:launchpad",
                    string.Empty,
                    string.Empty,
                    "Launchpad",
                    false,
                    false,
                    IntPtr.Zero,
                    DockSection.Pinned,
                    string.Empty,
                    Kind: DockTileKind.Launchpad);
                var trash = new DockAppItem(
                    "builtin:trash",
                    "shell:RecycleBinFolder",
                    string.Empty,
                    Strings.Dock_RecycleBin,
                    false,
                    false,
                    IntPtr.Zero,
                    DockSection.Running,
                    string.Empty,
                    Kind: DockTileKind.Trash);
                ImageSource pad = DockIconFactory.Load(icons, launchpad, 32);
                ImageSource bin = DockIconFactory.Load(icons, trash, 32);
                Assert.NotSame(pad, bin);
                Assert.IsAssignableFrom<BitmapSource>(pad);
                Assert.True(((BitmapSource)pad).PixelWidth >= 16);
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

    private static void AssertFaces()
    {
        var apps = new List<LaunchpadApp>();
        for (int i = 1; i <= 30; i++)
        {
            apps.Add(new LaunchpadApp($"Uygulama {i:00}", $@"C:\app{i}.lnk"));
        }

        LaunchpadApp? opened = null;
        LaunchpadSession bare = Session(apps, LaunchpadStyles.Win11, _ => { }, app => opened = app);
        UIElement bareFace = LaunchpadWin11Face.Create(bare);
        Assert.DoesNotContain("Mesajlar", Texts(bareFace));
        Assert.DoesNotContain(Strings.Launchpad_Pinned, Texts(bareFace));

        var pins = new List<LaunchpadApp> { new("Sabit Uygulama", @"C:\pin.lnk") };
        var recent = new List<LaunchpadRecentItem>
        {
            new("notlar", @"C:\recent\notlar.lnk", DateTime.Now.AddHours(-2)),
        };
        LaunchpadSession session = Session(
            apps,
            LaunchpadStyles.Win11,
            _ => { },
            app => opened = app,
            pins,
            recent,
            new LaunchpadPhone("Pixel"));

        UIElement win11 = LaunchpadWin11Face.Create(session);
        Assert.Equal(680, ((FrameworkElement)win11).Width);
        Assert.Contains("Uygulama 01", Texts(win11));
        Assert.Contains("U", Texts(win11));
        Assert.Contains(Strings.Launchpad_All, Texts(win11));
        Assert.Contains(Strings.Launchpad_Pinned, Texts(win11));
        Assert.Contains(Strings.Launchpad_Recent, Texts(win11));
        Assert.Contains("notlar", Texts(win11));
        Assert.DoesNotContain("Mesajlar", Texts(win11));
        Assert.Contains("Uygulama, ayar ve belge ara", Texts(win11));
        Assert.DoesNotContain("Mac", Texts(win11));
        ClickApp(win11, "Uygulama 01");
        Assert.Equal("Uygulama 01", opened?.Name);
        Assert.Contains(Strings.Launchpad_ToolDocuments, ToolTips(win11));
        Assert.Contains(Strings.Launchpad_ToolPower, ToolTips(win11));
        var card = Assert.IsType<Border>(win11);
        Assert.Equal(22, card.Padding.Left);
        Assert.Equal(18, card.Padding.Bottom);
        Assert.True(card.Padding.Bottom > card.CornerRadius.BottomLeft);
    }

    [Fact]
    public void Footer_ShowsChosenTools_AndCanHideAll()
    {
        RunSta(AssertFooterTools);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Deactivate_ClosesOnlyWhenThePadIsReady(bool acceptDismiss, bool closing, bool expected)
    {
        Assert.Equal(expected, LaunchpadWindow.CanCloseFromDeactivate(acceptDismiss, closing));
    }

    private static void AssertFooterTools()
    {
        LaunchpadSession session = Session(
            [new LaunchpadApp("Uygulama", @"C:\app.lnk")],
            LaunchpadStyles.Win11,
            _ => { },
            _ => { });

        UIElement all = LaunchpadWin11Face.Create(session);
        Assert.Equal(
            [Strings.Launchpad_ToolDocuments, Strings.Launchpad_ToolDownloads, Strings.Launchpad_ToolPictures, Strings.Launchpad_ToolSettings, Strings.Launchpad_ToolWidgets, Strings.Launchpad_ToolPlugins, Strings.Launchpad_ToolPower],
            ToolTips(all));
        Button icon = FindButtons(all).First(button => Equals(button.ToolTip, Strings.Launchpad_ToolDocuments));
        Assert.Equal(40, icon.Width);
        Assert.Equal(40, icon.Height);
        Assert.True(icon.Padding.Left >= 4);
        Assert.Equal(1, UserChips(all));

        session.Config.Launchpad.Tools = ["settings"];
        UIElement only = LaunchpadWin11Face.Create(session);
        Assert.Equal([Strings.Launchpad_ToolSettings], ToolTips(only));
        Assert.Equal(1, UserChips(only));

        session.Config.Launchpad.Tools = [];
        UIElement none = LaunchpadWin11Face.Create(session);
        Assert.Empty(ToolTips(none));
        Assert.Equal(1, UserChips(none));

        session.Config.Launchpad.Tools = LaunchpadTools.DefaultIds.ToList();
        UIElement host = LaunchpadWin11Face.Create(session);
        ContextMenu menu = FindToolMenu(host);
        Assert.Equal(LaunchpadTools.DefaultIds.Count, menu.Items.Count);
        Assert.All(menu.Items.Cast<object>(), item =>
        {
            var choice = Assert.IsType<MenuItem>(item);
            Assert.True(choice.IsCheckable);
            Assert.True(choice.IsChecked);
        });

        MenuItem power = menu.Items.Cast<MenuItem>().Single(item => Equals(item.Header, Strings.Launchpad_ToolPower));
        power.IsChecked = false;
        power.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.DoesNotContain(LaunchpadTools.Power, session.Config.Launchpad.Tools);
        Assert.DoesNotContain(Strings.Launchpad_ToolPower, ToolTips(host));
        Assert.Contains(Strings.Launchpad_ToolDocuments, ToolTips(host));
        Assert.Equal(1, UserChips(host));
        Assert.Null(FindButtons(host).First(button => Equals(button.ToolTip, Strings.Launchpad_ToolDocuments)).ContextMenu);

        foreach (MenuItem item in menu.Items.Cast<MenuItem>())
        {
            item.IsChecked = false;
        }

        power.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Empty(session.Config.Launchpad.Tools);
        Assert.Empty(ToolTips(host));
        Assert.Equal(1, UserChips(host));
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

    private static int UserChips(DependencyObject root) =>
        FindButtons(root).Count(button =>
            button.Content is StackPanel panel
            && panel.Orientation == Orientation.Horizontal
            && panel.Children.Count == 2
            && panel.Children[0] is Border);

    private static List<string> ToolTips(DependencyObject root)
    {
        var known = new HashSet<string>(LaunchpadTools.DefaultIds.Select(LaunchpadTools.Label), StringComparer.Ordinal);
        var tips = new List<string>();
        foreach (Button button in FindButtons(root))
        {
            if (button.ToolTip is string tip && known.Contains(tip))
            {
                tips.Add(tip);
            }
        }

        return tips;
    }

    private static ContextMenu FindToolMenu(DependencyObject root)
    {
        foreach (DependencyObject node in Walk(root))
        {
            if (node is FrameworkElement element
                && element.ContextMenu is { } menu
                && menu.Items.Count == LaunchpadTools.DefaultIds.Count)
            {
                return menu;
            }
        }

        throw new InvalidOperationException("Launchpad tool menu was not built.");
    }

    private static LaunchpadSession Session(
        IReadOnlyList<LaunchpadApp> apps,
        string style,
        Action<string> useStyle,
        Action<LaunchpadApp> open,
        IReadOnlyList<LaunchpadApp>? pinned = null,
        IReadOnlyList<LaunchpadRecentItem>? recent = null,
        LaunchpadPhone? phone = null)
    {
        return new LaunchpadSession
        {
            Config = new AppConfig { Launchpad = new LaunchpadConfig { Style = style } },
            Apps = apps,
            Icon = _ => new DrawingImage(),
            Open = open,
            Close = () => { },
            UseStyle = useStyle,
            OpenWindowsSettings = () => { },
            OpenWharfSettings = () => { },
            OpenWidgets = () => { },
            OpenPlugins = () => { },
            OpenPath = _ => { },
            Pinned = pinned ?? [],
            Recent = recent ?? [],
            Phone = phone,
            LinkedPhone = phone is { } named
                ? new LaunchpadPhoneSnapshot(named.Name, null, null, true, false, [])
                : null,
        };
    }

    private static void Click(DependencyObject root, string content)
    {
        Button? button = FindButtons(root).FirstOrDefault(candidate => Equals(candidate.Content, content));
        Assert.NotNull(button);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static void ClickApp(DependencyObject root, string name)
    {
        Button? button = FindButtons(root).FirstOrDefault(candidate => Texts(candidate).Contains(name));
        Assert.NotNull(button);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static List<string> Texts(DependencyObject root)
    {
        var texts = new List<string>();
        foreach (DependencyObject node in Walk(root))
        {
            if (node is TextBlock block && block.Text.Length > 0)
            {
                texts.Add(block.Text);
            }

            if (node is ContentControl control && control.Content is string label && label.Length > 0)
            {
                texts.Add(label);
            }
        }

        return texts;
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        foreach (DependencyObject node in Walk(root))
        {
            if (node is Button button)
            {
                yield return button;
            }
        }
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject node)
    {
        yield return node;
        foreach (object child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is not DependencyObject next)
            {
                continue;
            }

            foreach (DependencyObject descendant in Walk(next))
            {
                yield return descendant;
            }
        }
    }
}
