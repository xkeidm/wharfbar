using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WharfBar.Native;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Windows 11 style launchpad box: a pinned row, letter-grouped app grid, recent files,
/// and a footer with the account chip and tools. The phone panel is drawn by the host window
/// beside this box.
/// </summary>
internal static class LaunchpadWin11Face
{
    internal static UIElement Create(LaunchpadSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new LaunchpadFace(session).Build();
    }
}

/// <summary>
/// One live launchpad face. Owns the search box, the scrolling body, keyboard navigation,
/// and the footer tools; apps are opened through <see cref="LaunchpadSession"/>.
/// </summary>
internal sealed class LaunchpadFace
{
    private const int PinnedCap = 6;
    private const int RecentPreview = 4;

    private static readonly Brush TransparentFill = Brushes.Transparent;
    private static readonly ControlTemplate FlatTemplate = CreateFlatTemplate();

    private readonly LaunchpadSession _session;
    private readonly int _appColumns;
    private readonly int _recentColumns;
    private readonly TextBox _search;
    private readonly TextBlock _placeholder;
    private readonly Button _clear;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _body;
    private readonly StackPanel _tools;
    private readonly Border _root;

    private readonly List<Button> _items = [];
    private readonly List<LaunchpadNavSection> _sections = [];
    private int _focused = -1;
    private bool _showAllRecent;
    private bool _grid = true;

    internal LaunchpadFace(LaunchpadSession session)
    {
        _session = session;
        // Columns follow the real card width; the 520 DIP minimum card would otherwise
        // wrap at four app tiles while the keyboard math assumed six.
        double inner = Math.Max(1, _session.CardWidth - 44 - 8);
        _appColumns = Math.Max(1, (int)(inner / 100));
        _recentColumns = Math.Max(1, (int)(inner / 314));

        _search = new TextBox
        {
            MinHeight = 38,
            Padding = new Thickness(38, 6, 36, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1),
            FontSize = 14,
        };
        _search.SetResourceReference(Control.BackgroundProperty, "ControlFillColorDefaultBrush");
        _search.SetResourceReference(Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        _search.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        _search.SetResourceReference(TextBox.CaretBrushProperty, "TextFillColorPrimaryBrush");
        AutomationProperties.SetName(_search, Strings.Launchpad_SearchPlaceholder);

        _placeholder = Caption(Strings.Launchpad_SearchPlaceholder, 14);
        _placeholder.Margin = new Thickness(38, 0, 36, 0);
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.IsHitTestVisible = false;
        _placeholder.Opacity = 0.55;

        _clear = new Button
        {
            Content = Glyph("\uE711", 11),
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Background = TransparentFill,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = FlatTemplate,
            FocusVisualStyle = null,
            IsTabStop = false,
        };
        _clear.SetResourceReference(Control.ForegroundProperty, "TextFillColorSecondaryBrush");
        _clear.Click += (_, _) =>
        {
            _search.Text = string.Empty;
            _search.Focus();
        };

        _body = new StackPanel();
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            IsTabStop = false,
            Focusable = false,
            Content = _body,
        };

        _tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 40,
            MinHeight = 40,
        };

        _root = new Border
        {
            Width = _session.CardWidth,
            MaxHeight = _session.MaxCardHeight,
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22, 16, 22, 18),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        _root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        _root.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        _root.Loaded += (_, _) => ApplySheen(_root);
        _root.PreviewKeyDown += OnPreviewKeyDown;
        _root.PreviewTextInput += OnPreviewTextInput;
    }

    internal Border Build()
    {
        _search.TextChanged += (_, _) => Refresh(ScrollMode.Top);
        _search.PreviewKeyDown += OnSearchKeyDown;
        _search.Loaded += (_, _) => _search.Focus();
        _session.Reload = () => Refresh(ScrollMode.Keep);
        _session.SearchText = () => _search.Text;

        UIElement searchHost = SearchHost();
        UIElement footer = Footer();
        var dock = new DockPanel();
        DockPanel.SetDock(searchHost, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(searchHost);
        dock.Children.Add(footer);
        dock.Children.Add(_scroll);

        _scroll.MaxHeight = LaunchpadLayout.ScrollHeight(_session.MaxCardHeight);
        _root.Child = dock;
        Refresh(ScrollMode.Top);
        return _root;
    }

    private UIElement SearchHost()
    {
        var glyph = Glyph("\uE721", 14);
        glyph.Margin = new Thickness(12, 0, 0, 0);
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        glyph.IsHitTestVisible = false;

        var host = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        host.Children.Add(_search);
        host.Children.Add(_placeholder);
        host.Children.Add(glyph);
        host.Children.Add(_clear);
        return host;
    }

    private UIElement Footer()
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
        AutomationProperties.SetName(user, Environment.UserName);
        user.Click += (_, _) => _session.OpenWindowsSettings();
        bar.Children.Add(user);

        FillTools();
        _tools.ContextMenu = ToolMenu();
        Grid.SetColumn(_tools, 1);
        bar.Children.Add(_tools);
        return bar;
    }

    private void FillTools()
    {
        _tools.Children.Clear();
        string? previousGroup = null;
        foreach (string id in LaunchpadTools.Normalize(_session.Config.Launchpad.Tools))
        {
            string group = ToolGroup(id);
            if (previousGroup is not null && !string.Equals(previousGroup, group, StringComparison.Ordinal))
            {
                var divider = new Border
                {
                    Width = 1,
                    Height = 18,
                    Margin = new Thickness(7, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                divider.SetResourceReference(Border.BackgroundProperty, "ControlStrokeColorDefaultBrush");
                _tools.Children.Add(divider);
            }

            _tools.Children.Add(id == LaunchpadTools.Power
                ? PowerButton()
                : FolderButton(LaunchpadTools.Glyph(id), LaunchpadTools.Label(id), OpenTool(id)));
            previousGroup = group;
        }
    }

    private static string ToolGroup(string id) => id switch
    {
        LaunchpadTools.Documents or LaunchpadTools.Downloads or LaunchpadTools.Pictures => "folders",
        LaunchpadTools.Settings or LaunchpadTools.Widgets or LaunchpadTools.Plugins => "system",
        _ => "power",
    };

    private Action OpenTool(string id) => id switch
    {
        LaunchpadTools.Documents => () => _session.OpenPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        LaunchpadTools.Downloads => OpenDownloads,
        LaunchpadTools.Pictures => () => _session.OpenPath(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
        LaunchpadTools.Settings => _session.OpenWindowsSettings,
        LaunchpadTools.Widgets => _session.OpenWidgets,
        LaunchpadTools.Plugins => _session.OpenPlugins,
        _ => static () => { },
    };

    private void OpenDownloads()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length == 0)
        {
            return;
        }

        _session.OpenPath(System.IO.Path.Combine(profile, "Downloads"));
    }

    private ContextMenu ToolMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = _tools,
            Placement = PlacementMode.Top,
        };
        var shown = new HashSet<string>(
            LaunchpadTools.Normalize(_session.Config.Launchpad.Tools),
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
            item.Click += (_, _) => ApplyToolMenu(menu);
            menu.Items.Add(item);
        }

        return menu;
    }

    private void ApplyToolMenu(ContextMenu menu)
    {
        var next = new List<string>();
        foreach (object entry in menu.Items)
        {
            if (entry is MenuItem choice && choice.IsChecked && choice.Tag is string id)
            {
                next.Add(id);
            }
        }

        _session.Config.Launchpad.Tools = next;
        if (Application.Current is App app)
        {
            app.Config?.Save(_session.Config);
        }

        FillTools();
    }

    // ---------------------------------------------------------------- content

    private enum ScrollMode
    {
        Top,
        Keep,
    }

    private void Refresh(ScrollMode scrollMode)
    {
        double offset = _scroll.VerticalOffset;
        bool hadFocus = _root.IsKeyboardFocusWithin;
        int previousFocus = _focused;
        _items.Clear();
        _sections.Clear();
        _focused = -1;
        _placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _clear.Visibility = _search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _body.Children.Clear();

        string needle = _search.Text.Trim();
        if (needle.Length > 0)
        {
            FillResults(needle);
        }
        else
        {
            FillPinned();
            FillRecent();
            FillAllApps();
        }

        if (_body.Children.Count == 0 && !_session.CatalogPending)
        {
            _body.Children.Add(EmptyState(Strings.Launchpad_NoApps));
        }

        // A rebuild replaces the buttons that held focus (view toggle, show all, catalog
        // refresh). Put the keyboard back where it was so typing still reaches the search.
        if (hadFocus && !ReferenceEquals(Keyboard.FocusedElement, _search))
        {
            if (previousFocus >= 0 && previousFocus < _items.Count)
            {
                FocusItem(previousFocus);
            }
            else
            {
                _search.Focus();
            }
        }

        if (scrollMode == ScrollMode.Top)
        {
            _scroll.ScrollToTop();
            return;
        }

        if (offset > 0)
        {
            _scroll.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _scroll.ScrollToVerticalOffset(offset));
        }
    }

    private void FillResults(string needle)
    {
        List<LaunchpadApp> results = Matches(needle);
        AddHeaderRow(
            $"{Strings.Launchpad_Results} ({results.Count})",
            ViewToggle());

        if (results.Count > 0)
        {
            _body.Children.Add(_grid ? TileGrid(_appColumns, results.Select(app => AppTile(app, large: false)))
                : ListRows(results));
        }
        else if (!_session.CatalogPending)
        {
            _body.Children.Add(EmptyState(Strings.Launchpad_NoMatches));
        }

        List<LaunchpadRecentItem> recent = MatchRecent(needle);
        if (recent.Count > 0)
        {
            _body.Children.Add(Header(Strings.Launchpad_Recent));
            _body.Children.Add(TileGrid(_recentColumns, recent.Select(RecentTile)));
        }
    }

    private List<LaunchpadApp> Matches(string needle)
    {
        var results = new List<LaunchpadApp>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LaunchpadApp app in _session.Pinned.Concat(_session.Apps))
        {
            if (!LaunchpadSearch.Contains(app.Name, needle))
            {
                continue;
            }

            string name = app.Name.Trim();
            string path = app.ShortcutPath.Trim();
            if (!seenNames.Add(name) || (!string.IsNullOrEmpty(path) && !seenPaths.Add(path)))
            {
                continue;
            }

            results.Add(app);
        }

        return LaunchpadCatalog.Rank(results, needle).ToList();
    }

    private List<LaunchpadRecentItem> MatchRecent(string needle)
    {
        return LaunchpadRecent
            .Clean(_session.Recent)
            .Where(item => LaunchpadSearch.Contains(item.Name, needle))
            .ToList();
    }

    private void FillPinned()
    {
        List<LaunchpadApp> pins = _session.Pinned
            .Where(pin => !string.IsNullOrWhiteSpace(pin.ShortcutPath))
            .Take(PinnedCap)
            .ToList();
        if (pins.Count == 0)
        {
            return;
        }

        _body.Children.Add(Header(Strings.Launchpad_Pinned));
        _body.Children.Add(TileGrid(_appColumns, pins.Select(app => AppTile(app, large: true))));
    }

    private void FillAllApps()
    {
        if (_session.Apps.Count == 0)
        {
            return;
        }

        AddHeaderRow(Strings.Launchpad_All, ViewToggle());
        foreach (LaunchpadLetterGroup group in LaunchpadCatalog.GroupByLetter(_session.Apps))
        {
            _body.Children.Add(LetterHeader(group.Letter));
            _body.Children.Add(_grid ? TileGrid(_appColumns, group.Apps.Select(app => AppTile(app, large: false)))
                : ListRows(group.Apps));
        }
    }

    private void FillRecent()
    {
        IReadOnlyList<LaunchpadRecentItem> recent = LaunchpadRecent.Clean(_session.Recent);
        if (recent.Count == 0)
        {
            return;
        }

        IReadOnlyList<LaunchpadRecentItem> shown = _showAllRecent
            ? recent
            : recent.Take(RecentPreview).ToArray();
        UIElement? action = recent.Count > RecentPreview
            ? TextButton(_showAllRecent ? Strings.Launchpad_ShowLess : Strings.Launchpad_ShowAll, () =>
            {
                _showAllRecent = !_showAllRecent;
                Refresh(ScrollMode.Keep);
            })
            : null;
        AddHeaderRow(Strings.Launchpad_Recent, action);
        _body.Children.Add(TileGrid(_recentColumns, shown.Select(RecentTile)));
    }

    private static UIElement EmptyState(string text)
    {
        var block = Caption(text, 13);
        block.Opacity = 0.7;
        block.Margin = new Thickness(6, 26, 6, 26);
        block.HorizontalAlignment = HorizontalAlignment.Center;
        return block;
    }

    // ---------------------------------------------------------------- tiles

    private Button AppTile(LaunchpadApp app, bool large)
    {
        double width = large ? 96 : 92;
        double iconSize = large ? 44 : 40;
        var icon = new Image
        {
            Width = iconSize,
            Height = iconSize,
            Source = SafeIcon(app),
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
        AutomationProperties.SetName(button, app.Name);
        button.Click += (_, _) => _session.Open(app);
        Hover(button);
        return button;
    }

    private ImageSource? SafeIcon(LaunchpadApp app)
    {
        try
        {
            return _session.Icon(app);
        }
        catch (Exception ex)
        {
            // A broken shortcut must not take the process down (same class as the catalog
            // gotcha: the shell API throws on the UI thread).
            Debug.WriteLine($"WharfBar launchpad icon: {app.ShortcutPath}: {ex.Message}");
            return null;
        }
    }

    private Button RecentTile(LaunchpadRecentItem item)
    {
        var icon = new Image
        {
            Width = 28,
            Height = 28,
            Source = SafeIcon(new LaunchpadApp(item.Name, item.Path)),
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
        AutomationProperties.SetName(button, item.Name);
        button.Click += (_, _) => _session.OpenPath(item.Path);
        Hover(button);
        return button;
    }

    private Button ListRow(LaunchpadApp app)
    {
        var icon = new Image
        {
            Width = 20,
            Height = 20,
            Source = SafeIcon(app),
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
        button.ToolTip = app.Name;
        AutomationProperties.SetName(button, app.Name);
        button.Click += (_, _) => _session.Open(app);
        Hover(button);
        return button;
    }

    private UIElement ListRows(IEnumerable<LaunchpadApp> apps)
    {
        var stack = new StackPanel();
        foreach (LaunchpadApp app in apps)
        {
            stack.Children.Add(ListRow(app));
        }

        return stack;
    }

    private Button PowerButton()
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
            _session.Close();
        }));
        menu.Items.Add(PowerItem(Strings.TopBar_Sleep, () => PowerActions.TrySleep()));
        menu.Items.Add(PowerItem(Strings.TopBar_Shutdown, () =>
        {
            PowerActions.TryShutdown();
            _session.Close();
        }));
        menu.Items.Add(PowerItem(Strings.TopBar_Restart, () =>
        {
            PowerActions.TryRestart();
            _session.Close();
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

    private Button FolderButton(string glyph, string tip, Action open)
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
        AutomationProperties.SetName(button, tip);
        Hover(button);
        return button;
    }

    private Button ViewToggle()
    {
        var button = Flat();
        button.Width = 30;
        button.Height = 30;
        button.Padding = new Thickness(5);
        button.Margin = new Thickness(2, 0, 0, 0);
        button.Content = Glyph(_grid ? "\uE8FD" : "\uE80A", 14);
        button.ToolTip = _grid ? Strings.Launchpad_ViewList : Strings.Launchpad_ViewGrid;
        AutomationProperties.SetName(button, button.ToolTip as string ?? string.Empty);
        button.Click += (_, _) =>
        {
            _grid = !_grid;
            Refresh(ScrollMode.Keep);
        };
        Hover(button);
        return button;
    }

    private static Button TextButton(string label, Action click)
    {
        var button = Flat();
        button.Content = label;
        button.FontSize = 12;
        button.Padding = new Thickness(8, 4, 8, 4);
        button.SetResourceReference(Control.ForegroundProperty, "AccentFillColorDefaultBrush");
        button.Click += (_, _) => click();
        return button;
    }

    // ---------------------------------------------------------------- sections

    private WrapPanel TileGrid(int columns, IEnumerable<Button> tiles)
    {
        int first = _items.Count;
        var wrap = new WrapPanel();
        foreach (Button tile in tiles)
        {
            wrap.Children.Add(tile);
            _items.Add(tile);
        }

        if (_items.Count > first)
        {
            _sections.Add(new LaunchpadNavSection(first, _items.Count - first, columns));
        }

        return wrap;
    }

    private void AddHeaderRow(string text, UIElement? action)
    {
        var row = new Grid { Margin = new Thickness(4, 12, 4, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock title = Header(text);
        title.Margin = new Thickness(0);
        row.Children.Add(title);
        if (action is not null)
        {
            if (action is FrameworkElement element)
            {
                element.VerticalAlignment = VerticalAlignment.Center;
            }

            Grid.SetColumn(action, 1);
            row.Children.Add(action);
        }

        _body.Children.Add(row);
    }

    private static TextBlock Header(string text)
    {
        var block = Caption(text, 14);
        block.FontWeight = FontWeights.SemiBold;
        block.Margin = new Thickness(6, 8, 6, 4);
        return block;
    }

    private static TextBlock LetterHeader(string letter)
    {
        var block = Caption(letter, 18);
        block.FontWeight = FontWeights.SemiBold;
        block.Margin = new Thickness(6, 12, 6, 4);
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
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

    // ---------------------------------------------------------------- keyboard

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _search.Text.Length > 0)
        {
            _search.Text = string.Empty;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _items.Count > 0)
        {
            e.Handled = true;
            ClickItem(0);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _search.Text.Length > 0)
        {
            _search.Text = string.Empty;
            e.Handled = true;
            _search.Focus();
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (ReferenceEquals(Keyboard.FocusedElement, _search))
            {
                if (_items.Count > 0)
                {
                    e.Handled = true;
                    ClickItem(0);
                }

                return;
            }

            // A focused Button activates on Space, not Enter; the pad treats both as "open".
            if (_focused >= 0)
            {
                e.Handled = true;
                ClickItem(_focused);
            }

            return;
        }

        LaunchpadMove? move = e.Key switch
        {
            Key.Up => LaunchpadMove.Up,
            Key.Down => LaunchpadMove.Down,
            Key.Left => LaunchpadMove.Left,
            Key.Right => LaunchpadMove.Right,
            _ => null,
        };
        if (move is null)
        {
            return;
        }

        if (ReferenceEquals(Keyboard.FocusedElement, _search))
        {
            if (move == LaunchpadMove.Down && _items.Count > 0)
            {
                e.Handled = true;
                FocusItem(0);
            }

            return;
        }

        e.Handled = true;
        MoveFocus(move.Value);
    }

    private void MoveFocus(LaunchpadMove move)
    {
        if (_items.Count == 0)
        {
            return;
        }

        if (_focused < 0)
        {
            FocusItem(move is LaunchpadMove.Up ? _items.Count - 1 : 0);
            return;
        }

        int next = LaunchpadTileNav.Move(_sections, _focused, move);
        if (next < 0)
        {
            _focused = -1;
            _search.Focus();
            return;
        }

        FocusItem(next);
    }

    private void FocusItem(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        _focused = index;
        Button button = _items[index];
        button.Focus();
        button.BringIntoView();
    }

    private void ClickItem(int index)
    {
        if (index >= 0 && index < _items.Count)
        {
            _items[index].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.OriginalSource is TextBox || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        char first = e.Text[0];
        if (char.IsControl(first))
        {
            return;
        }

        _search.Focus();
        _search.CaretIndex = _search.Text.Length;
        _search.SelectedText = e.Text;
        e.Handled = true;
    }

    // ---------------------------------------------------------------- chrome

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

    private static void Hover(Button button)
    {
        button.ApplyTemplate();

        if (button.Template?.FindName("HoverOverlay", button) is not Border overlay)
        {
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
        gradient.GradientStops.Add(new GradientStop(Blend(fill.Color, accent, 0.14), 0));
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
        // Border takes one child, so presenter + overlays share a grid. The padding stays
        // on the root border; the grid stretches across it.
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
        // Keyboard focus ring, shown by the template trigger for IsKeyboardFocused.
        var ring = new FrameworkElementFactory(typeof(Border), "FocusRing");
        ring.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("AccentFillColorDefaultBrush"));
        ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        ring.SetValue(Border.MarginProperty, new Thickness(-2));
        ring.SetValue(Border.OpacityProperty, 0.0);
        ring.SetValue(FrameworkElement.IsHitTestVisibleProperty, false);
        align.AppendChild(ring);
        border.AppendChild(align);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var focusTrigger = new Trigger
        {
            Property = UIElement.IsKeyboardFocusedProperty,
            Value = true,
        };
        focusTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "FocusRing"));
        template.Triggers.Add(focusTrigger);
        return template;
    }

    private static ControlTemplate CreateFlatTemplate()
    {
        ControlTemplate template = BuildFlatTemplate();
        template.Seal();
        return template;
    }
}
