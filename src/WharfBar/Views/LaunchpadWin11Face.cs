using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WharfBar.Resources;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Windows 11 Start box: search, pins, recent files, all apps,
/// and a footer with the account and power. The phone panel sits beside this box.
/// </summary>
internal static class LaunchpadWin11Face
{
    private const double CardWidth = 680;
    private const int PinnedCap = 8;
    private const int RecentPreview = 6;

    private static readonly Brush TransparentFill = Brushes.Transparent;

    // Sealed so a second STA (unit tests each spin their own) can share the template.
    // An unfrozen ControlTemplate belongs to the thread that first touched this type.
    private static readonly ControlTemplate FlatTemplate = CreateFlatTemplate();

    internal static UIElement Create(LaunchpadSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var search = new TextBox
        {
            MinHeight = 36,
            Padding = new Thickness(36, 6, 12, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1),
            FontSize = 14,
        };
        search.SetResourceReference(Control.BackgroundProperty, "ControlFillColorDefaultBrush");
        search.SetResourceReference(Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        search.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        search.SetResourceReference(TextBox.CaretBrushProperty, "TextFillColorPrimaryBrush");

        var placeholder = Caption(Strings.Launchpad_SearchPlaceholder, 14);
        placeholder.Margin = new Thickness(36, 0, 12, 0);
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.IsHitTestVisible = false;
        placeholder.Opacity = 0.55;

        var searchGlyph = Glyph("\uE721", 14);
        searchGlyph.Margin = new Thickness(12, 0, 0, 0);
        searchGlyph.HorizontalAlignment = HorizontalAlignment.Left;
        searchGlyph.IsHitTestVisible = false;

        var searchHost = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchHost.Children.Add(search);
        searchHost.Children.Add(placeholder);
        searchHost.Children.Add(searchGlyph);

        var body = new StackPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 480,
            Content = body,
        };

        bool showAllRecent = false;
        bool grid = true;

        void Refresh()
        {
            placeholder.Visibility = string.IsNullOrEmpty(search.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            body.Children.Clear();
            Fill(body, session, search.Text, () => showAllRecent, value => showAllRecent = value, () => grid, value => grid = value, Refresh);
        }

        search.TextChanged += (_, _) => Refresh();
        search.Loaded += (_, _) => search.Focus();
        session.Reload = Refresh;
        Refresh();

        var dock = new DockPanel();
        DockPanel.SetDock(searchHost, Dock.Top);
        var footer = Footer(session);
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(searchHost);
        dock.Children.Add(footer);
        dock.Children.Add(scroll);

        var root = new Border
        {
            Width = CardWidth,
            MaxHeight = 740,
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22, 16, 22, 18),
            Child = dock,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        root.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        root.Loaded += (_, _) => ApplySheen(root);
        return root;
    }

    private static void Fill(
        StackPanel body,
        LaunchpadSession session,
        string? query,
        Func<bool> showAllRecent,
        Action<bool> setShowAllRecent,
        Func<bool> grid,
        Action<bool> setGrid,
        Action refresh)
    {
        string needle = query?.Trim() ?? string.Empty;
        bool searching = needle.Length > 0;

        List<LaunchpadApp> pins = Match(session.Pinned, needle).Take(PinnedCap).ToList();
        if (pins.Count > 0)
        {
            body.Children.Add(Header(Strings.Launchpad_Pinned));
            body.Children.Add(Wrap(pins.Select(app => AppTile(session, app, large: true))));
        }

        List<LaunchpadRecentItem> recent = [];
        foreach (LaunchpadRecentItem item in session.Recent)
        {
            if (!searching || item.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase))
            {
                recent.Add(item);
            }
        }

        if (recent.Count > 0)
        {
            bool expanded = showAllRecent() || searching;
            int shown = expanded ? recent.Count : Math.Min(RecentPreview, recent.Count);
            var row = new Grid { Margin = new Thickness(4, 14, 4, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Header(Strings.Launchpad_Recent);
            title.Margin = new Thickness(0);
            row.Children.Add(title);
            if (!searching && recent.Count > RecentPreview)
            {
                var more = TextButton(showAllRecent() ? Strings.Launchpad_ShowLess : Strings.Launchpad_ShowAll);
                more.Click += (_, _) =>
                {
                    setShowAllRecent(!showAllRecent());
                    refresh();
                };
                Grid.SetColumn(more, 1);
                row.Children.Add(more);
            }

            body.Children.Add(row);
            var recentWrap = new WrapPanel();
            foreach (LaunchpadRecentItem item in recent.Take(shown))
            {
                recentWrap.Children.Add(RecentTile(session, item));
            }

            body.Children.Add(recentWrap);
        }

        List<LaunchpadApp> apps = Match(session.Apps, needle);
        var allHeader = new Grid { Margin = new Thickness(4, 14, 4, 4) };
        allHeader.ColumnDefinitions.Add(new ColumnDefinition());
        allHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var allTitle = Header(searching ? Strings.Launchpad_Results : Strings.Launchpad_All);
        allTitle.Margin = new Thickness(0);
        allHeader.Children.Add(allTitle);
        var view = TextButton(grid() ? Strings.Launchpad_ViewGrid : Strings.Launchpad_ViewList);
        view.Click += (_, _) =>
        {
            setGrid(!grid());
            refresh();
        };
        Grid.SetColumn(view, 1);
        allHeader.Children.Add(view);
        body.Children.Add(allHeader);

        if (apps.Count == 0)
        {
            if (searching || !session.CatalogPending)
            {
                body.Children.Add(Caption(Strings.Launchpad_NoMatches, 13));
            }

            return;
        }

        foreach (LaunchpadLetterGroup group in LaunchpadCatalog.GroupByLetter(apps))
        {
            body.Children.Add(LetterHeader(group.Letter));
            if (!grid() && !searching)
            {
                foreach (LaunchpadApp app in group.Apps)
                {
                    body.Children.Add(ListRow(session, app));
                }
            }
            else
            {
                body.Children.Add(Wrap(group.Apps.Select(app => AppTile(session, app, large: false))));
            }
        }
    }

    private static TextBlock LetterHeader(string letter)
    {
        var block = Caption(letter, 18);
        block.FontWeight = FontWeights.SemiBold;
        block.Margin = new Thickness(6, 18, 6, 6);
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return block;
    }

    private static UIElement Footer(LaunchpadSession session)
    {
        var bar = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var user = Flat();
        user.HorizontalAlignment = HorizontalAlignment.Left;
        user.Padding = new Thickness(4, 4, 10, 4);
        var userRow = new StackPanel { Orientation = Orientation.Horizontal };
        userRow.Children.Add(Avatar(Environment.UserName));
        var userName = Caption(Environment.UserName, 13);
        userName.Margin = new Thickness(8, 0, 0, 0);
        userName.VerticalAlignment = VerticalAlignment.Center;
        userRow.Children.Add(userName);
        user.Content = userRow;
        user.Click += (_, _) => session.OpenWindowsSettings();
        bar.Children.Add(user);

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 40,
            MinHeight = 40,
        };
        FillTools(tools, session);
        tools.ContextMenu = ToolMenu(session, tools);
        Grid.SetColumn(tools, 1);
        bar.Children.Add(tools);
        return bar;
    }

    private static void FillTools(StackPanel tools, LaunchpadSession session)
    {
        tools.Children.Clear();
        foreach (string id in LaunchpadTools.Normalize(session.Config.Launchpad.Tools))
        {
            tools.Children.Add(id == LaunchpadTools.Power
                ? PowerButton(session)
                : FolderButton(LaunchpadTools.Glyph(id), LaunchpadTools.Label(id), OpenTool(session, id)));
        }
    }

    private static Action OpenTool(LaunchpadSession session, string id) => id switch
    {
        LaunchpadTools.Documents => () => session.OpenPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        LaunchpadTools.Downloads => () => OpenDownloads(session),
        LaunchpadTools.Pictures => () => session.OpenPath(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
        LaunchpadTools.Settings => session.OpenWindowsSettings,
        LaunchpadTools.Widgets => session.OpenWidgets,
        LaunchpadTools.Plugins => session.OpenPlugins,
        _ => static () => { },
    };

    private static ContextMenu ToolMenu(LaunchpadSession session, StackPanel tools)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = tools,
            Placement = PlacementMode.Top,
        };
        var shown = new HashSet<string>(
            LaunchpadTools.Normalize(session.Config.Launchpad.Tools),
            StringComparer.Ordinal);
        foreach (string id in LaunchpadTools.DefaultIds)
        {
            var item = new MenuItem
            {
                Header = LaunchpadTools.Label(id),
                IsCheckable = true,
                IsChecked = shown.Contains(id),
                StaysOpenOnClick = true,
                Tag = id,
            };
            item.Click += (_, _) => ApplyToolMenu(session, tools, menu);
            menu.Items.Add(item);
        }

        return menu;
    }

    private static void ApplyToolMenu(LaunchpadSession session, StackPanel tools, ContextMenu menu)
    {
        var next = new List<string>();
        foreach (object entry in menu.Items)
        {
            if (entry is MenuItem choice && choice.IsChecked && choice.Tag is string id)
            {
                next.Add(id);
            }
        }

        session.Config.Launchpad.Tools = next;
        if (Application.Current is App app)
        {
            app.Config?.Save(session.Config);
        }

        FillTools(tools, session);
    }

    private static void OpenDownloads(LaunchpadSession session)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length == 0)
        {
            return;
        }

        session.OpenPath(System.IO.Path.Combine(profile, "Downloads"));
    }

    private static Button AppTile(LaunchpadSession session, LaunchpadApp app, bool large)
    {
        double width = large ? 112 : 92;
        var icon = new Image
        {
            Width = large ? 44 : 40,
            Height = large ? 44 : 40,
            Source = session.Icon(app),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
        var name = new TextBlock
        {
            Text = app.Name,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 32,
            MaxWidth = width - 8,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        stack.Children.Add(icon);
        stack.Children.Add(name);

        var button = Flat();
        button.Width = width;
        button.MinHeight = large ? 92 : 78;
        button.Margin = new Thickness(2);
        button.Padding = new Thickness(4, 8, 4, 6);
        button.Content = stack;
        button.ToolTip = app.Name;
        button.Click += (_, _) => session.Open(app);
        Hover(button);
        return button;
    }

    private static Button RecentTile(LaunchpadSession session, LaunchpadRecentItem item)
    {
        var icon = new Image
        {
            Width = 28,
            Height = 28,
            Source = session.Icon(new LaunchpadApp(item.Name, item.Path)),
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 10, 0),
            IsHitTestVisible = false,
        };
        var name = Caption(item.Name, 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var when = Caption(LaunchpadRecent.FormatAge(item.Written, DateTime.Now), 11);
        when.Opacity = 0.65;
        var text = new StackPanel { IsHitTestVisible = false };
        text.Children.Add(name);
        text.Children.Add(when);
        var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        row.Children.Add(icon);
        row.Children.Add(text);

        var button = Flat();
        button.Width = 310;
        button.MinHeight = 48;
        button.Margin = new Thickness(2);
        button.Padding = new Thickness(8, 4, 8, 4);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Content = row;
        button.ToolTip = item.Name;
        button.Click += (_, _) => session.OpenPath(item.Path);
        Hover(button);
        return button;
    }

    private static Button ListRow(LaunchpadSession session, LaunchpadApp app)
    {
        var icon = new Image
        {
            Width = 20,
            Height = 20,
            Source = session.Icon(app),
            Margin = new Thickness(0, 0, 10, 0),
            IsHitTestVisible = false,
        };
        var name = Caption(app.Name, 13);
        name.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        row.Children.Add(icon);
        row.Children.Add(name);
        var button = Flat();
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.MinHeight = 36;
        button.Margin = new Thickness(2, 1, 2, 1);
        button.Content = row;
        button.Click += (_, _) => session.Open(app);
        Hover(button);
        return button;
    }

    private static Button PowerButton(LaunchpadSession session)
    {
        var button = IconButton(LaunchpadTools.Glyph(LaunchpadTools.Power), LaunchpadTools.Label(LaunchpadTools.Power));
        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Top,
        };
        menu.Items.Add(PowerItem(Strings.TopBar_Lock, () =>
        {
            PowerActions.TryLock();
            session.Close();
        }));
        menu.Items.Add(PowerItem(Strings.TopBar_Sleep, () => PowerActions.TrySleep()));
        menu.Items.Add(PowerItem(Strings.TopBar_Shutdown, () =>
        {
            PowerActions.TryShutdown();
            session.Close();
        }));
        menu.Items.Add(PowerItem(Strings.TopBar_Restart, () =>
        {
            PowerActions.TryRestart();
            session.Close();
        }));
        menu.Closed += (_, _) => button.ContextMenu = null;
        button.Click += (_, _) => button.Dispatcher.InvokeAsync(() =>
        {
            button.ContextMenu = menu;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        });
        return button;
    }

    private static MenuItem PowerItem(string label, Action activate)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => activate();
        return item;
    }

    private static Button FolderButton(string glyph, string tip, Action open)
    {
        var button = IconButton(glyph, tip);
        button.Click += (_, _) => open();
        return button;
    }

    private static Button IconButton(string glyph, string tip)
    {
        var button = Flat();
        button.Width = 40;
        button.Height = 40;
        button.Padding = new Thickness(6);
        button.Margin = new Thickness(2, 0, 2, 0);
        var icon = Glyph(glyph, 16);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.TextAlignment = TextAlignment.Center;
        button.Content = icon;
        button.ToolTip = tip;
        Hover(button);
        return button;
    }

    private static Button TextButton(string label, Action? click = null)
    {
        var button = Flat();
        button.Content = label;
        button.FontSize = 12;
        button.Padding = new Thickness(8, 4, 8, 4);
        button.SetResourceReference(Control.ForegroundProperty, "AccentFillColorDefaultBrush");
        if (click is not null)
        {
            button.Click += (_, _) => click();
        }

        return button;
    }

    private static WrapPanel Wrap(IEnumerable<UIElement> tiles)
    {
        var wrap = new WrapPanel();
        foreach (UIElement tile in tiles)
        {
            wrap.Children.Add(tile);
        }

        return wrap;
    }

    private static TextBlock Header(string text)
    {
        var block = Caption(text, 14);
        block.FontWeight = FontWeights.SemiBold;
        block.Margin = new Thickness(6, 8, 6, 4);
        return block;
    }

    private static TextBlock Caption(string text, double size)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        return block;
    }

    private static TextBlock Glyph(string code, double size)
    {
        var block = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return block;
    }

    private static Border Avatar(string name)
    {
        string letter = name.Length == 0 ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();
        var disk = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Child = new TextBlock
            {
                Text = letter,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        disk.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        if (disk.Child is TextBlock text)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        }

        return disk;
    }

    private static void ApplySheen(Border root)
    {
        if (root.TryFindResource("SolidBackgroundFillColorBaseBrush") is not SolidColorBrush fill)
        {
            return;
        }

        Color accent = root.TryFindResource("AccentFillColorDefaultBrush") is SolidColorBrush accentBrush
            ? accentBrush.Color
            : fill.Color;
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        gradient.GradientStops.Add(new GradientStop(Blend(fill.Color, accent, 0.16), 0));
        gradient.GradientStops.Add(new GradientStop(fill.Color, 0.28));
        gradient.GradientStops.Add(new GradientStop(fill.Color, 1));
        gradient.Freeze();
        root.Background = gradient;
    }

    private static Color Blend(Color basis, Color tint, double amount)
    {
        byte Mix(byte from, byte to) => (byte)Math.Clamp(from + ((to - from) * amount), 0, 255);
        return Color.FromRgb(Mix(basis.R, tint.R), Mix(basis.G, tint.G), Mix(basis.B, tint.B));
    }

    private static List<LaunchpadApp> Match(IReadOnlyList<LaunchpadApp> apps, string needle)
    {
        var next = new List<LaunchpadApp>();
        foreach (LaunchpadApp app in apps)
        {
            if (needle.Length == 0 || app.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase))
            {
                next.Add(app);
            }
        }

        return next;
    }

    private static void Hover(Button button)
    {
        button.ApplyTemplate();

        // The flat template is sealed and shared, so the overlay is resolved per
        // instance after ApplyTemplate. The resource swap stays as the fallback.
        if (button.Template?.FindName("HoverOverlay", button) is not Border overlay)
        {
            button.MouseEnter += (_, _) =>
                button.SetResourceReference(Control.BackgroundProperty, "ControlFillColorSecondaryBrush");
            button.MouseLeave += (_, _) => button.Background = TransparentFill;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        button.MouseEnter += (_, _) =>
            overlay.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
        button.MouseLeave += (_, _) =>
            overlay.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        Press(button);
    }

    private static void Press(Button button)
    {
        var scale = new ScaleTransform(1, 1);
        button.RenderTransform = scale;
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        var down = new DoubleAnimation(0.97, TimeSpan.FromMilliseconds(60));
        button.PreviewMouseLeftButtonDown += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, down);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, down);
        };
        button.PreviewMouseLeftButtonUp += (_, _) => Release(scale);
    }

    private static void Release(ScaleTransform scale)
    {
        var up = new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, up);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
    }

    private static Button Flat() =>
        new()
        {
            Background = TransparentFill,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = FlatTemplate,
            FocusVisualStyle = null,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

    private static ControlTemplate BuildFlatTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        // Border takes one child, so presenter + hover overlay share a grid. The
        // padding stays on the root border; the grid stretches across it.
        var align = new FrameworkElementFactory(typeof(Grid));
        align.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        align.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        align.AppendChild(presenter);
        // Painted over the content but not hit-testable; Hover() fades it in so the
        // fill eases instead of snapping. Theme lookup must stay dynamic: the sealed,
        // shared template cannot resolve a resource at seal time.
        var hover = new FrameworkElementFactory(typeof(Border), "HoverOverlay");
        hover.SetValue(Border.BackgroundProperty, new DynamicResourceExtension("ControlFillColorSecondaryBrush"));
        hover.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        hover.SetValue(Border.OpacityProperty, 0.0);
        hover.SetValue(FrameworkElement.IsHitTestVisibleProperty, false);
        align.AppendChild(hover);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static ControlTemplate CreateFlatTemplate()
    {
        ControlTemplate template = BuildFlatTemplate();
        template.Seal();
        return template;
    }
}
