using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// Ada (pano geçmişi + bırakılan dosyalar) ayarları: görünürlük, saklama modu, limitler ve
/// bakım eylemleri. Her değişiklik config'i kaydeder ve <see cref="IslandService.ApplyConfig"/>
/// ile canlı uygular — <c>Config.Enabled</c> yalnızca ApplyConfig üzerinden değiştiği için
/// pano dinleyicisi bu çağrı olmadan hiç açılmaz, pill görünürlüğü de güncellenmez.
/// </summary>
public partial class IslandPage : Page
{
    private bool _isLoading = true;
    private bool _syncingLimits;

    public IslandPage()
    {
        InitializeComponent();
        LoadSettings();

        EnabledToggle.Checked += OnEnabledChanged;
        EnabledToggle.Unchecked += OnEnabledChanged;
        PersistenceCombo.SelectionChanged += OnPersistenceChanged;
        RetentionBox.LostFocus += OnRetentionLostFocus;
        MaxItemsBox.LostFocus += OnMaxItemsLostFocus;

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;

    private ConfigService Config => WharfApp.Config!;

    private AppConfig Cfg => Config.Current;

    /// <summary>
    /// <see cref="MonitorManager.Engine"/> Dispose sonrasında throw eder (App kapanırken ayar
    /// penceresi hâlâ açık olabiliyor); bu yüzden <see langword="null"/> dönebilir ve bakım
    /// butonları sessizce no-op olur. <see cref="TopBarEngine.Island"/> aksi hâlde her zaman
    /// doludur — servis <c>Engine</c> kurucusunda koşulsuz yaratılır.
    /// </summary>
    private IslandService? Island
    {
        get
        {
            try
            {
                return WharfApp.Monitors?.Engine.Island;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    private void LoadSettings()
    {
        IslandConfig island = Cfg.Island;
        EnabledToggle.IsChecked = island.Enabled;
        PersistenceCombo.SelectedIndex = IsPersistent(island.Persistence) ? 1 : 0;

        // IslandNormalizer her zaman 1..3650 / 10..5000'e sıkıştırır; kutular da aynı değeri
        // gösterir (elle config.json'a yazılmış taşma değerler kullanıcıyı şaşırtmasın).
        _syncingLimits = true;
        RetentionBox.Text = IslandNormalizer.ClampRetentionDays(island.RetentionDays)
            .ToString(CultureInfo.InvariantCulture);
        MaxItemsBox.Text = IslandNormalizer.ClampMaxItems(island.MaxItems)
            .ToString(CultureInfo.InvariantCulture);
        _syncingLimits = false;

        UpdateLimitsEnabled();
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Island.Enabled = EnabledToggle.IsChecked == true;
        SaveAndApply();
    }

    private void OnPersistenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || PersistenceCombo.SelectedIndex < 0)
        {
            return;
        }

        // Persistence bir string ("session" / "persistent"), enum değil: seçim indeksini
        // normalizasyon katmanından geçirip kanonik sabitlere bağlıyoruz.
        Cfg.Island.Persistence = PersistenceCombo.SelectedIndex == 1
            ? IslandConfig.PersistencePersistent
            : IslandConfig.PersistenceSession;

        UpdateLimitsEnabled();
        SaveAndApply();
    }

    private void OnRetentionLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _syncingLimits)
        {
            return;
        }

        if (TryCommitLimit(RetentionBox, IslandNormalizer.ClampRetentionDays, out int days))
        {
            Cfg.Island.RetentionDays = days;
            SaveAndApply();
        }
    }

    private void OnMaxItemsLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _syncingLimits)
        {
            return;
        }

        if (TryCommitLimit(MaxItemsBox, IslandNormalizer.ClampMaxItems, out int maxItems))
        {
            Cfg.Island.MaxItems = maxItems;
            SaveAndApply();
        }
    }

    /// <summary>
    /// Kutuya yazılan metni <paramref name="clamp"/> ile sıkıştırıp config'e yazılacak değere
    /// çevirir; kutuya sıkıştırılmış hâlini geri yazar (kullanıcı sınırı aştığını görsün).
    /// Ayrıştırılamayan girdide kullanıcının metnine dokunmaz, yalnız hata gösterir — yarım
    /// yazılmış bir sayı ("36") sessizce 3'e uygulanmasın.
    /// </summary>
    private bool TryCommitLimit(TextBox box, Func<int, int> clamp, out int value)
    {
        value = 0;
        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            LimitsErrorText.Text = "Geçerli bir tam sayı girin.";
            LimitsErrorText.Visibility = Visibility.Visible;
            return false;
        }

        value = clamp(parsed);
        LimitsErrorText.Visibility = Visibility.Collapsed;
        box.Text = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// Gün limiti yalnızca kalıcı modda uygulanır (<c>IslandPruner.Prune</c>:
    /// <c>persistent &amp;&amp; retentionDays &gt; 0</c>), bu yüzden oturumluk modda kutu
    /// kilitlenir. Kayıt sayısı tavanı her iki modda da çalıştığı için açık kalır.
    /// </summary>
    private void UpdateLimitsEnabled()
    {
        RetentionBox.IsEnabled = PersistenceCombo.SelectedIndex == 1;
    }

    private void OnOpenDropFolderClick(object sender, RoutedEventArgs e)
    {
        // IslandStore.DropRoot ile aynı yol; ConfigService tek doğruluk kaynağı.
        string root = Path.Combine(Config.ConfigDirectory, "Drop");
        try
        {
            Directory.CreateDirectory(root);
            Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WharfBar] open drop folder failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Dosya kayıtlarını listeden düşürür ve <c>Drop</c> klasörünü tarayarak boşaltır. Yalnız
    /// <c>Clear</c> çağırmak yanlış olurdu: o, pano metinlerini de siler ve kullanıcı "dosyaları
    /// sil" derken geçmişin tamamını kaybetmiş olur.
    /// </summary>
    private void OnDeleteDroppedFilesClick(object sender, RoutedEventArgs e) =>
        Island?.DeleteDroppedFiles();

    /// <summary>Listeyi boşaltır; dosya kopyalarına dokunmaz (bilinçli, bkz. IslandService.Clear).</summary>
    private void OnClearHistoryClick(object sender, RoutedEventArgs e) => Island?.Clear();

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        Island?.ApplyConfig(Cfg.Island);
    }

    private static bool IsPersistent(string? persistence) =>
        string.Equals(
            IslandNormalizer.NormalizePersistence(persistence),
            IslandConfig.PersistencePersistent,
            StringComparison.Ordinal);
}