using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views.Onboarding;

/// <summary>
/// Setup step: pick a desktop background from the bundled WharfBar pack or the user's own
/// wallpapers. Every click goes straight through a step-owned <see cref="WallpaperService"/>,
/// so the real Windows wallpaper behind the wizard changes immediately. The auto accent color
/// stays off during setup (<c>AccentEnabled = false</c>); Settings → Island turn it back on.
/// </summary>
public partial class WallpaperStep : UserControl
{
    private const double ThumbWidthDip = 168;
    private const double ThumbHeightDip = 94;
    private const double ThumbGapDip = 8;
    private const int ThumbDecodeWidthPx = 336;

    /// <summary>
    /// Decoded thumbnails, shared by the step instances of one wizard session (the caches are
    /// dropped when the step unloads, so they never outlive the wizard). Keys carry the file's
    /// write time so an edited wallpaper is never served from a stale entry; the bitmaps are frozen
    /// and safe across threads.
    /// </summary>
    private static readonly ConcurrentDictionary<string, BitmapSource> ThumbCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>In-flight decodes, so a revisit (or a prewarm) never decodes the same file twice.</summary>
    private static readonly ConcurrentDictionary<string, Task<BitmapSource?>> PendingThumbs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Background decodes at a time; two keeps the UI responsive while the gallery fills.</summary>
    private static readonly SemaphoreSlim DecodeSlots = new(2, 2);

    /// <summary>Upper bound for the shared thumbnail cache.</summary>
    private const int ThumbCacheLimit = 96;

    private static int _prewarmed;

    /// <summary>
    /// Bumped whenever the shared caches are dropped. A decode that was already running (or queued)
    /// at that moment may not insert its thumbnail afterwards, otherwise an in-flight prewarm would
    /// refill the "cleared" cache and the memory would stay pinned after the wizard closed.
    /// </summary>
    private static int _cacheGeneration;

    private readonly WallpaperService _service;
    private readonly Dictionary<string, (Border Tile, Border Badge)> _tilesByPath =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _currentPath;

    public WallpaperStep()
    {
        InitializeComponent();

        // Step-owned service: no transition overlay, no hub/accent side effects during setup.
        _service = new WallpaperService(WallpaperPaths.BundledRoot, WallpaperPaths.UserRoot)
        {
            AccentEnabled = () => false,
        };

        LoadGallery();

        // The wizard builds a fresh step per visit, so both caches are process-wide statics. Drop
        // them when this step leaves the tree: otherwise up to ~24 MB of decoded thumbnails stay
        // pinned for the whole process after the wizard closes (or after jumping to another step).
        Unloaded += OnStepUnloaded;
        Loaded += OnStepLoaded;
    }

    /// <summary>
    /// Also drops the caches when the wizard closes while another step is the current content: then
    /// no <see cref="WallpaperStep"/> unloads and a late prewarm would leave the thumbnails pinned.
    /// A static handler avoids retaining this step instance in the window's event list.
    /// </summary>
    private void OnStepLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.Closed -= OnHostWindowClosed;
            window.Closed += OnHostWindowClosed;
        }
    }

    /// <summary>Releases the shared thumbnail caches; only the onboarding gallery uses them.</summary>
    private static void OnStepUnloaded(object sender, RoutedEventArgs e) => DropThumbCaches();

    private static void OnHostWindowClosed(object? sender, EventArgs e) => DropThumbCaches();

    private static void DropThumbCaches()
    {
        // Bump first: a decode that is already running must not refill the map right after the clear.
        Interlocked.Increment(ref _cacheGeneration);
        ThumbCache.Clear();
        PendingThumbs.Clear();
    }

    /// <summary>
    /// Decodes every gallery thumbnail in the background once per process. The setup window calls
    /// this a couple of seconds after it opens (once the first step has painted), so the wallpaper
    /// step paints instantly — and never decodes ~20 photos inside its own page transition — while
    /// the opening moments stay free of background decode work.
    /// </summary>
    internal static void Prewarm(string bundledRoot, string userRoot)
    {
        if (Interlocked.Exchange(ref _prewarmed, 1) == 1)
        {
            return;
        }

        foreach (WallpaperItem item in WallpaperCatalog.List(bundledRoot, userRoot))
        {
            _ = GetThumbAsync(item.Path);
        }
    }

    private void LoadGallery()
    {
        BundledPanel.Children.Clear();
        YoursPanel.Children.Clear();
        _tilesByPath.Clear();

        IReadOnlyList<WallpaperItem> items = WallpaperCatalog.List(_service.BundledRoot, _service.UserRoot);
        List<WallpaperItem> bundled = items.Where(i => i.Source == WallpaperSource.Bundled).ToList();
        List<WallpaperItem> user = items.Where(i => i.Source == WallpaperSource.User).ToList();

        _currentPath = _service.TryGetCurrentPath();

        BundledHeader.Visibility = bundled.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        YoursHeader.Visibility = user.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        AddTiles(BundledPanel, bundled);
        AddTiles(YoursPanel, user);

        UpdateCurrentWallpaperLabel(_currentPath ?? _service.TryGetDesktopWallpaperPath());
    }

    private void AddTiles(Panel host, List<WallpaperItem> items)
    {
        foreach (WallpaperItem item in items)
        {
            host.Children.Add(BuildTile(item));
        }
    }

    private UIElement BuildTile(WallpaperItem item)
    {
        string full = Path.GetFullPath(item.Path);
        bool selected = IsSelected(full);

        var tile = new Border
        {
            Width = ThumbWidthDip,
            Height = ThumbHeightDip,
            Margin = new Thickness(0, 0, ThumbGapDip, ThumbGapDip),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = item.DisplayName,
            Tag = full,
        };

        var imageHolder = new Border
        {
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = CreateThumbVisual(full),
        };

        var badge = new Border
        {
            Width = 15,
            Height = 15,
            CornerRadius = new CornerRadius(7.5),
            Visibility = selected ? Visibility.Visible : Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 3, 0),
            IsHitTestVisible = false,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        var badgeGlyph = new TextBlock
        {
            Text = "\u2713",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // On-accent tone: the accent fill flips between light and dark themes.
        badgeGlyph.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        badge.Child = badgeGlyph;

        var content = new Grid();
        content.Children.Add(imageHolder);
        content.Children.Add(badge);
        tile.Child = content;

        if (selected)
        {
            tile.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        }
        else
        {
            tile.BorderBrush = Brushes.Transparent;
        }

        // Hover is a quiet outline that never fights the accent ring of the selected tile.
        tile.MouseEnter += (_, _) =>
        {
            if (!IsSelected(full))
            {
                tile.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorSecondaryBrush");
            }
        };
        tile.MouseLeave += (_, _) =>
        {
            if (!IsSelected(full))
            {
                tile.BorderBrush = Brushes.Transparent;
            }
        };

        _tilesByPath[full] = (tile, badge);
        tile.MouseLeftButtonUp += Tile_MouseLeftButtonUp;
        return tile;
    }

    /// <summary>
    /// Placeholder now, photo as soon as its background decode finishes: the step builds in a few
    /// milliseconds instead of statically decoding every bundled wallpaper (~0.5 s).
    /// </summary>
    private UIElement CreateThumbVisual(string path)
    {
        var host = new Grid();
        host.Children.Add(CreatePlaceholder());

        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false,
        };
        host.Children.Add(image);

        _ = LoadThumbAsync(path, image);
        return host;
    }

    private static async Task LoadThumbAsync(string path, Image target)
    {
        try
        {
            BitmapSource? bmp = await GetThumbAsync(path).ConfigureAwait(false);
            if (bmp is null || target.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            // Marshal explicitly: the decode runs on a worker thread and the step may be created
            // outside a running dispatcher (render harness), where no SynchronizationContext exists.
            // Fire-and-forget is intended (the placeholder stays until the frame lands).
            _ = target.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => target.Source = bmp));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // A vanished or locked file keeps the placeholder; it must not break the step.
            Debug.WriteLine($"WallpaperStep thumbnail failed: {ex.Message}");
        }
    }

    private static Task<BitmapSource?> GetThumbAsync(string path)
    {
        string key = CacheKey(path);
        if (ThumbCache.TryGetValue(key, out BitmapSource? cached))
        {
            return Task.FromResult<BitmapSource?>(cached);
        }

        // One decode per file even when a revisit happens mid-decode. The cache generation is
        // captured now: a decode started before a cache clear may not insert its result afterwards.
        int generation = Volatile.Read(ref _cacheGeneration);
        return PendingThumbs.GetOrAdd(
            key,
            k => Task.Run(() => DecodeThumbAsync(k, path, generation)));
    }

    private static async Task<BitmapSource?> DecodeThumbAsync(string key, string path, int generation)
    {
        await DecodeSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            BitmapImage? bmp = TryDecode(path, ThumbDecodeWidthPx);
            if (bmp is not null
                && generation == Volatile.Read(ref _cacheGeneration)
                && ThumbCache.Count < ThumbCacheLimit)
            {
                // Bounded: a large user wallpaper folder must not pin every thumbnail for the
                // whole process lifetime (uncached ones simply decode again on a later visit).
                ThumbCache[key] = bmp;
            }

            return bmp;
        }
        finally
        {
            DecodeSlots.Release();
            PendingThumbs.TryRemove(key, out _);
        }
    }

    private static string CacheKey(string path)
    {
        long ticks;
        try
        {
            ticks = File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ticks = 0;
        }

        return $"{path}|{ticks}";
    }

    private static UIElement CreatePlaceholder()
    {
        var border = new Border();
        border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");

        var glyph = new TextBlock
        {
            Text = "\uE91B",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "AppIconFontFamily");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        border.Child = glyph;
        return border;
    }

    private void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string path })
        {
            return;
        }

        e.Handled = true;

        bool applied;
        try
        {
            applied = _service.TryApply(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"WallpaperStep apply failed: {ex.Message}");
            applied = false;
        }

        if (!applied)
        {
            StatusText.Text = Strings.Onboarding_Wallpaper_Failed;
            StatusRow.Visibility = Visibility.Visible;
            return;
        }

        StatusRow.Visibility = Visibility.Collapsed;
        _currentPath = path;
        UpdateSelection(path);
        UpdateCurrentWallpaperLabel(path);
    }

    private void UpdateSelection(string appliedPath)
    {
        foreach (KeyValuePair<string, (Border Tile, Border Badge)> entry in _tilesByPath)
        {
            if (string.Equals(entry.Key, appliedPath, StringComparison.OrdinalIgnoreCase))
            {
                entry.Value.Tile.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
                entry.Value.Badge.Visibility = Visibility.Visible;
            }
            else
            {
                entry.Value.Tile.BorderBrush = Brushes.Transparent;
                entry.Value.Badge.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdateCurrentWallpaperLabel(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            CurrentWallpaperRow.Visibility = Visibility.Collapsed;
            CurrentWallpaperText.Text = string.Empty;
            return;
        }

        CurrentWallpaperRow.Visibility = Visibility.Visible;
        CurrentWallpaperText.Text = string.Format(
            Strings.Onboarding_Wallpaper_CurrentFormat,
            Path.GetFileName(path));
    }

    private bool IsSelected(string fullPath) =>
        _currentPath is not null
        && string.Equals(fullPath, _currentPath, StringComparison.OrdinalIgnoreCase);

    private static BitmapImage? TryDecode(string path, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException
                                       or UnauthorizedAccessException or InvalidOperationException
                                       or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _service.EnsureUserRoot();
            Process.Start(new ProcessStartInfo { FileName = _service.UserRoot, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"WallpaperStep open folder failed: {ex.Message}");
        }
    }
}
