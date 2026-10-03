using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Island paneli: arama kutusu + tek birleşik zaman çizelgesi (pano metni ve bırakılan
/// dosyalar). Projede MVVM yok; satırlar <see cref="Border"/> olarak elle kurulur.
/// </summary>
public sealed partial class IslandFlyout : UserControl
{
    /// <summary>
    /// Tek seferde kurulan satır tavanı. <c>MaxItems</c> 5000'e kadar çıkabiliyor ve liste
    /// sanalize değil: her tuş vuruşunda 5000 satır yeniden kurmak kayıtlı bir AppBar olan
    /// üst barı takar. Kurulmayan kayıt sayısı alt bilgide bildirilir, arama yine tüm kayıtlara
    /// bakar (sıra "en yeni başta" olduğu için ilk 200 eşleşme en yenilerdir).
    /// </summary>
    private const int MaxRenderedRows = 200;

    /// <summary>Bırakılan dosya kaydı — emoji, font bağımlılığı yok (MDL2 kodu doğrulanmadan yazılmaz).</summary>
    internal const string FileGlyph = "\uD83D\uDCC4";

    /// <summary>Pano metni kaydı.</summary>
    internal const string TextGlyph = "\u2261";

    private IslandService? _service;
    private readonly List<IslandEntry> _rows = [];
    private int _selectedIndex = -1;

    public IslandFlyout()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Servisi bağlar; önceki bağlantıyı koparır (idempotent). Listeyi kurmaz: tek çağıran
    /// <c>OnIslandPopupOpened</c> hemen ardından <see cref="OnPanelOpened"/>'ı çağırıyor,
    /// kurulum zaten orada yapılıyor.
    /// </summary>
    internal void Attach(IslandService service)
    {
        Detach();
        _service = service;
        service.Changed += OnChanged;
    }

    internal void Detach()
    {
        if (_service is not null)
        {
            _service.Changed -= OnChanged;
            _service = null;
        }

        _rows.Clear();
        _selectedIndex = -1;
        EntryListPanel.Children.Clear();
    }

    /// <summary>Panel açıldı: yüzeyi tazele, aramayı sıfırla, en yeni kaydı seç.</summary>
    internal void OnPanelOpened()
    {
        // Yarı saydam yüzey her açılışta yeniden üretilir (MediaFlyout ile aynı): tema panel
        // kapalıyken değişmiş olabilir.
        FlyoutRoot.Background = FlyoutSurface.Create();
        SearchBox.Clear();
        _selectedIndex = 0;
        Rebuild();
        SearchBox.Focus();

        // Popup kendi penceresini Opened'ten sonra gösteriyor; odak ilk denemede
        // yapışmazsa bir sonraki dispatcher turunda yeniden denenir. Kullanıcının tıklaması
        // bu arada olamaz (Input önceliği, sıradaki mesajdan önce çalışır).
        if (!SearchBox.IsFocused)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => SearchBox.Focus()));
        }
    }

    /// <summary>
    /// Panel kapandı. Canlı abonelik bırakılır: gizliyken her kopyalamada listeyi yeniden
    /// kurmak, kayıtlı bir AppBar olan üst bar için bedava iş değildir.
    /// </summary>
    internal void OnPanelClosed() => Detach();

    /// <summary>
    /// <see cref="IslandService.Changed"/> yalnızca UI dispatcher'ında yükselir
    /// (<c>IslandService.RaiseChanged</c>); atlamaya gerek yok.
    /// </summary>
    private void OnChanged(object? sender, EventArgs e) => Rebuild();

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (_service is null)
        {
            return;
        }

        string? query = SearchBox.Text;
        List<IslandEntry> matches = _service.Entries
            .Where(entry => IslandPruner.Matches(entry, query))
            .ToList();

        int shown = Math.Min(matches.Count, MaxRenderedRows);
        if (_selectedIndex >= shown)
        {
            _selectedIndex = shown - 1;
        }

        _rows.Clear();
        for (int i = 0; i < shown; i++)
        {
            _rows.Add(matches[i]);
        }

        // Tema fırçası her kurulumda değil, her yeniden kurulumda bir kez çözülür: 200 satır
        // için 200 kez kaynak aramak gereksiz.
        Brush selectedBrush = ResolveSelectedBrush();

        EntryListPanel.Children.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            EntryListPanel.Children.Add(BuildRow(_rows[i], i == _selectedIndex, selectedBrush));
        }

        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateHint(matches.Count, shown);
        ScrollSelectedIntoView();
    }

    private Border BuildRow(IslandEntry entry, bool selected, Brush selectedBrush)
    {
        Brush restingBrush = selected ? selectedBrush : Brushes.Transparent;
        var row = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(8),
            Background = restingBrush,
            Cursor = Cursors.Hand,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = entry.Kind == IslandEntryKind.File ? FileGlyph : TextGlyph,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var text = new TextBlock
        {
            Text = IslandPruner.FormatPreview(entry.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var meta = new TextBlock
        {
            Text = BuildMeta(entry),
            Opacity = 0.6,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(meta, 2);
        grid.Children.Add(meta);

        row.Child = grid;

        // Dosya satırında yalnız ad görünür; tam yol tooltip'te (kopyanın nerede olduğu
        // listeden anlaşılmıyor).
        if (entry.Kind == IslandEntryKind.File && entry.FilePath is { Length: > 0 } path)
        {
            row.ToolTip = path;
        }

        row.MouseEnter += (_, _) => row.Background = selectedBrush;
        row.MouseLeave += (_, _) => row.Background = restingBrush;
        row.MouseLeftButtonUp += (_, _) => Activate(entry);
        return row;
    }

    /// <summary>Satır sağındaki "kaynak uygulama · göreli zaman" etiketi.</summary>
    private static string BuildMeta(IslandEntry entry)
    {
        string relative = IslandPruner.FormatRelativeTime(entry.CapturedAtUtc, DateTimeOffset.UtcNow);
        if (string.IsNullOrEmpty(entry.SourceApp))
        {
            return relative;
        }

        string app = entry.SourceApp.Length > IslandPruner.SourceAppMaxChars
            ? entry.SourceApp[..IslandPruner.SourceAppMaxChars]
            : entry.SourceApp;
        return string.Format(CultureInfo.CurrentCulture, "{0} · {1}", app, relative);
    }

    private static Brush ResolveSelectedBrush()
    {
        if (Application.Current.TryFindResource("ControlFillColorDefaultBrush") is Brush brush)
        {
            return brush;
        }

        // Tema sözlüğü yüklenmemişse (ilk açılış yarışı) seçim görünmez olmasın.
        var fallback = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        fallback.Freeze();
        return fallback;
    }

    /// <summary>Alt bilgi satırı: boş liste ya da sınır nedeniyle kurulmayan kayıt sayısı.</summary>
    private void UpdateHint(int total, int shown)
    {
        if (total == 0)
        {
            HintText.Text = SearchBox.Text.Length == 0 ? "Kayıt yok." : "Eşleşen kayıt yok.";
            HintText.Visibility = Visibility.Visible;
            return;
        }

        if (total > shown)
        {
            HintText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "… {0} kayıt daha var (arama ile daralt)",
                total - shown);
            HintText.Visibility = Visibility.Visible;
            return;
        }

        HintText.Visibility = Visibility.Collapsed;
    }

    private void ScrollSelectedIntoView()
    {
        if (_selectedIndex < 0 || _selectedIndex >= EntryListPanel.Children.Count)
        {
            return;
        }

        // Klavye ile gezinirken seçili satır kaydırma alanının dışında kalabiliyor.
        if (EntryListPanel.Children[_selectedIndex] is FrameworkElement row)
        {
            row.BringIntoView();
        }
    }

    private void Activate(IslandEntry entry)
    {
        if (_service is null)
        {
            return;
        }

        if (entry.Kind == IslandEntryKind.File)
        {
            OpenFile(entry.FilePath);
            return;
        }

        // Metin kaydı panoya geri konur. PutBack kendi kopyasını yeniden yakalamaz
        // (CanIncludeInClipboardHistory=0 yazıyor) — aksi halde aynı kayıt iki kez listelenirdi.
        _service.PutBack(entry);
    }

    /// <summary>
    /// Bırakılan kopyayı varsayılan uygulamayla açar; dosya silinmez.
    /// </summary>
    /// <remarks>
    /// Yol <c>island.json</c>'dan gelir, yani <b>güvenilmez</b> bir girdidir — aynı sebepten
    /// silme yolu <c>Drop</c> sınırını zorunlu tutuyor. Doğrulamadan çalıştırmak iki ayrı
    /// güvenlik sınırı bırakırdı: elle düzenlenmiş bir kayıt, kullanıcının hiç bırakmadığı
    /// bir dosyayı varsayılan uygulamasında açabilirdi.
    /// </remarks>
    private void OpenFile(string? path)
    {
        if (_service is null || !_service.IsInsideDropRoot(path))
        {
            if (path is not null)
            {
                Debug.WriteLine($"[WharfBar] island open skipped, outside Drop: {path}");
            }

            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Bozuk kısayol, erişim yok, varsayılan uygulama yok: kayıt listede kalır.
            Debug.WriteLine($"[WharfBar] island open failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Panel klavye gezinmesi. Arama kutusu odaktayken <c>PreviewKeyDown</c>'den gelir; tuş
    /// burada doğrudan servise iletilir, sahte <see cref="KeyEventArgs"/> kurulmaz.
    /// </summary>
    /// <returns>Tüketilen tuş için <c>true</c> (çağıran <c>e.Handled</c> yapar).</returns>
    internal bool HandleNavigationKey(Key key)
    {
        switch (key)
        {
            case Key.Up:
            case Key.Down:
                if (_rows.Count == 0)
                {
                    return false;
                }

                _selectedIndex = key == Key.Down
                    ? Math.Min(_rows.Count - 1, _selectedIndex + 1)
                    : Math.Max(0, _selectedIndex - 1);
                Rebuild();
                return true;

            case Key.Enter:
                if (_selectedIndex < 0 || _selectedIndex >= _rows.Count)
                {
                    return false;
                }

                Activate(_rows[_selectedIndex]);
                return true;

            case Key.Delete:
                if (_selectedIndex < 0 || _selectedIndex >= _rows.Count)
                {
                    return false;
                }

                _service?.Remove(_rows[_selectedIndex]);
                return true;

            default:
                return false;
        }
    }
}
