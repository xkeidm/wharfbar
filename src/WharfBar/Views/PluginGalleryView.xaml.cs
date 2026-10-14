using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WharfBar.Resources;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views;

internal enum PluginGalleryScope
{
    All,
    Plugins,
    Widgets,
}

/// <summary>
/// Fluent list of built-in plugins/widgets and local manifests.
/// Enabling a local id only writes <c>plugins.enabled</c>.
/// </summary>
internal partial class PluginGalleryView : UserControl
{
    private AppConfig? _config;
    private string _root = string.Empty;
    private PluginGalleryScope _scope = PluginGalleryScope.All;
    private IReadOnlyList<PluginCard> _cards = [];
    private bool _loading;

    public PluginGalleryView()
    {
        InitializeComponent();
    }

    internal void Bind(AppConfig config, string pluginsDirectory, PluginGalleryScope scope)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _root = pluginsDirectory ?? string.Empty;
        _scope = scope;
        Heading.Text = scope switch
        {
            PluginGalleryScope.Widgets => Strings.Gallery_Widgets,
            PluginGalleryScope.Plugins => Strings.Gallery_Plugins,
            _ => Strings.Gallery_PluginsAndWidgets,
        };
        Intro.Text = Strings.Gallery_Intro;
        Refresh();
    }

    internal void Refresh()
    {
        if (_config is null || _loading)
        {
            return;
        }

        _cards = PluginCatalog.Compose(_config, PluginCatalog.Discover(_root));
        Rebuild();
    }

    private void Rebuild()
    {
        _loading = true;
        try
        {
            Sections.Children.Clear();
            Sections.Children.Add(Explanation());

            string? kind = _scope switch
            {
                PluginGalleryScope.Plugins => PluginKinds.Plugin,
                PluginGalleryScope.Widgets => PluginKinds.Widget,
                _ => null,
            };
            IReadOnlyList<PluginCard> visible = PluginCatalog.Filter(_cards, kind);
            var builtIn = new List<PluginCard>();
            var local = new List<PluginCard>();
            foreach (PluginCard card in visible)
            {
                if (card.BuiltIn)
                {
                    builtIn.Add(card);
                }
                else
                {
                    local.Add(card);
                }
            }

            Sections.Children.Add(SectionLabel(Strings.Gallery_BuiltIn));
            if (builtIn.Count == 0)
            {
                Sections.Children.Add(Note(Strings.Gallery_NoBuiltIn));
            }
            else
            {
                foreach (PluginCard card in builtIn)
                {
                    Sections.Children.Add(Card(card));
                }
            }

            Sections.Children.Add(SectionLabel(Strings.Gallery_Local));
            if (local.Count == 0)
            {
                Sections.Children.Add(EmptyLocal());
            }
            else
            {
                foreach (PluginCard card in local)
                {
                    Sections.Children.Add(Card(card));
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private UIElement Explanation()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Gallery_Slots,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
        });
        stack.Children.Add(Chips());
        stack.Children.Add(Secondary(
            Strings.Gallery_SlotsExplanation,
            new Thickness(0, 10, 0, 0)));

        var open = new Wpf.Ui.Controls.Button
        {
            Content = Strings.Gallery_OpenFolder,
            Height = 32,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Folder24 },
        };
        open.Click += (_, _) => OpenPluginsFolder();
        stack.Children.Add(open);
        if (!string.IsNullOrWhiteSpace(_root))
        {
            stack.Children.Add(Secondary(_root, new Thickness(0, 8, 0, 0)));
        }

        return Chrome(stack);
    }

    private UIElement Chips()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        wrap.Children.Add(Chip(Strings.Gallery_SlotTopBar, "topBar"));
        wrap.Children.Add(Chip(Strings.Gallery_SlotDock, "dock"));
        wrap.Children.Add(Chip(Strings.Gallery_SlotDesktop, Strings.Gallery_ChipDesktopDetail));
        wrap.Children.Add(Chip(Strings.Gallery_SlotIsland, Strings.Gallery_ChipIslandDetail));
        return wrap;
    }

    private static UIElement Chip(string title, string detail)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
        });
        panel.Children.Add(Secondary(detail, new Thickness(0, 1, 0, 0)));
        var border = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 8, 0),
            Child = panel,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        border.BorderThickness = new Thickness(1);
        return border;
    }

    private UIElement EmptyLocal()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Gallery_EmptyLocalTitle,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
        });
        stack.Children.Add(Secondary(
            Strings.Gallery_EmptyLocalBody,
            new Thickness(0, 4, 0, 0)));
        stack.Children.Add(new TextBlock
        {
            Text = "{\n"
                + "  \"id\": \"" + Strings.Gallery_ExampleId + "\",\n"
                + "  \"name\": \"" + Strings.Gallery_ExampleName + "\",\n"
                + "  \"description\": \"" + Strings.Gallery_ExampleDescription + "\",\n"
                + "  \"kind\": \"widget\",\n"
                + "  \"slot\": \"topBar\"\n"
                + "}",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });
        return Chrome(stack);
    }

    private UIElement Card(PluginCard card)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = card.Kind == PluginKinds.Widget ? "\uE771" : "\uE912",
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "AppIconFontFamily");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentFillColorDefaultBrush");
        Grid.SetColumn(glyph, 0);

        string meta = PluginKinds.Label(card.Kind) + " \u00b7 " + PluginSlots.Label(card.Slot);
        if (!card.BuiltIn)
        {
            meta += " \u00b7 " + Strings.Gallery_RecordOnly;
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock
        {
            Text = card.Name,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(Secondary(meta, new Thickness(0, 2, 0, 0)));
        if (!string.IsNullOrWhiteSpace(card.Summary))
        {
            text.Children.Add(Secondary(card.Summary, new Thickness(0, 4, 0, 0)));
        }

        Grid.SetColumn(text, 1);

        var toggle = new Wpf.Ui.Controls.ToggleSwitch
        {
            IsChecked = card.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(toggle, card.Name);
        toggle.Checked += (_, _) => Commit(card.Id, true);
        toggle.Unchecked += (_, _) => Commit(card.Id, false);
        Grid.SetColumn(toggle, 2);

        grid.Children.Add(glyph);
        grid.Children.Add(text);
        grid.Children.Add(toggle);
        return Chrome(grid);
    }

    private void Commit(string id, bool enabled)
    {
        if (_loading || _config is null)
        {
            return;
        }

        if (!PluginCatalog.SetEnabled(_config, id, enabled))
        {
            Refresh();
            return;
        }

        if (Application.Current is App app)
        {
            app.Config?.Save(_config);
        }

        if (id is PluginCatalog.Clock or PluginCatalog.Calendar)
        {
            WidgetHost.Apply(_config);
        }

        if (id == PluginCatalog.CameraMirror)
        {
            CameraMirrorHost.Apply(_config.Island.CameraMirror);
        }
    }

    private void OpenPluginsFolder()
    {
        if (string.IsNullOrWhiteSpace(_root))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_root);
            Process.Start(new ProcessStartInfo
            {
                FileName = _root,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[WharfBar] Plugin folder open failed: {ex.Message}");
        }
    }

    private static UIElement SectionLabel(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 8),
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return block;
    }

    private static TextBlock Secondary(string text, Thickness margin)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return block;
    }

    private static UIElement Note(string text) => Secondary(text, new Thickness(0, 0, 0, 8));

    private static Border Chrome(UIElement child)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 8),
            Child = child,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return border;
    }
}
