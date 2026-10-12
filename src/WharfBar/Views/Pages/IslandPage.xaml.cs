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
        MusicPresenceCombo.SelectionChanged += OnMusicPresenceChanged;
        IdleLabelCombo.SelectionChanged += OnIdleLabelChanged;
        RetentionBox.LostFocus += OnRetentionLostFocus;
        MaxItemsBox.LostFocus += OnMaxItemsLostFocus;
        LowBatteryBox.LostFocus += OnLowBatteryLostFocus;

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
        MusicPresenceCombo.SelectedIndex = IslandNormalizer.IsTransientMusic(island.MusicPresence) ? 1 : 0;
        IdleLabelCombo.SelectedIndex = IslandNormalizer.IsLastClipIdle(island.IdleLabel) ? 1 : 0;

        // IslandNormalizer her zaman 1..3650 / 10..5000'e sıkıştırır; kutular da aynı değeri
        // gösterir (elle config.json'a yazılmış taşma değerler kullanıcıyı şaşırtmasın).
        _syncingLimits = true;
        RetentionBox.Text = IslandNormalizer.ClampRetentionDays(island.RetentionDays)
            .ToString(CultureInfo.InvariantCulture);
        MaxItemsBox.Text = IslandNormalizer.ClampMaxItems(island.MaxItems)
            .ToString(CultureInfo.InvariantCulture);
        int lowBattery = IslandNormalizer.NormalizeLowBatteryPercent(island.LowBatteryPercent);
        LowBatteryBox.Text = lowBattery.ToString(CultureInfo.InvariantCulture);
        LowBatteryValueText.Text = lowBattery.ToString(CultureInfo.InvariantCulture);
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

    private void OnMusicPresenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || MusicPresenceCombo.SelectedIndex < 0)
        {
            return;
        }

        Cfg.Island.MusicPresence = MusicPresenceCombo.SelectedIndex == 1
            ? IslandConfig.MusicPresenceTransient
            : IslandConfig.MusicPresencePersistent;
        SaveAndApply();
    }

    private void OnIdleLabelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || IdleLabelCombo.SelectedIndex < 0)
        {
            return;
        }

        Cfg.Island.IdleLabel = IdleLabelCombo.SelectedIndex == 1
            ? IslandConfig.IdleLabelLastClip
            : IslandConfig.IdleLabelClipboard;
        SaveAndApply();
    }

    private void OnRetentionLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _syncingLimits)
        {
            return;
        }

        if (TryCommitLimit(RetentionBox, IslandNormalizer.ClampRetentionDays, RetentionRangeText, out int days))
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

        if (TryCommitLimit(MaxItemsBox, IslandNormalizer.ClampMaxItems, MaxItemsRangeText, out int maxItems))
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
    /// <summary>Kutu altindaki aralik yazisiyla ayni — hata mesaji onunla celismemeli.</summary>
    private const string RetentionRangeText = "1–3650";

    private const string MaxItemsRangeText = "10–5000";

    private const string LowBatteryRangeText = "5–50";

    private void OnLowBatteryLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _syncingLimits)
        {
            return;
        }

        if (!int.TryParse(LowBatteryBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            LowBatteryErrorText.Text = $"{LowBatteryRangeText} aralığında bir tam sayı girin.";
            LowBatteryErrorText.Visibility = Visibility.Visible;
            return;
        }

        int percent = IslandNormalizer.ClampLowBatteryPercent(parsed);
        LowBatteryErrorText.Visibility = Visibility.Collapsed;
        LowBatteryBox.Text = percent.ToString(CultureInfo.InvariantCulture);
        LowBatteryValueText.Text = percent.ToString(CultureInfo.InvariantCulture);
        Cfg.Island.LowBatteryPercent = percent;
        SaveAndApply();
    }

    private bool TryCommitLimit(TextBox box, Func<int, int> clamp, string rangeText, out int value)
    {
        value = 0;
        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            // int.TryParse tasma girdide de false doner ("99999999999" gecerli bir tam sayidir ama
            // int'e sigmaz), o yuzden mesaj kutunun gercek araligini soylemeli — "gecerli bir tam
            // sayi girin" ile kutu altindaki aralik yazisi celisirdi.
            LimitsErrorText.Text = $"{rangeText} aralığında bir tam sayı girin.";
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
    /// <remarks>
    /// Spec bu eylemi "(onaylı)" diye tanımlıyor. <c>DeleteDroppedFiles</c> <c>Drop</c>
    /// klasörünü tarayarak <b>geri dönüşsüz</b> boşaltır, yani onaysız çalışması bir veri
    /// kaybı kuralı ihlalidir. Onay <see cref="ConfirmDestructive"/> ile alınır; onay
    /// alınamazsa eylem hiç yapılmaz (fail-closed).
    /// </remarks>
    private void OnDeleteDroppedFilesClick(object sender, RoutedEventArgs e)
    {
        if (Island is not { } island)
        {
            return;
        }

        if (!ConfirmDestructive(
                "Bırakılan dosyalar silinsin mi?",
                "%AppData%\\WharfBar\\Drop klasörü taranır ve içindeki TÜM kopyalar kalıcı "
                    + "olarak silinir.\n\nOrijinal dosyalarınız silinmez, ama kopyaları geri "
                    + "getirilemez."))
        {
            return;
        }

        island.DeleteDroppedFiles();
    }

    /// <summary>Listeyi boşaltır; dosya kopyalarına dokunmaz (bilinçli, bkz. IslandService.Clear).</summary>
    private void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        if (Island is not { } island)
        {
            return;
        }

        if (!ConfirmDestructive(
                "Tüm geçmiş temizlensin mi?",
                "Pano geçmişi ve bırakılan dosya kayıtları listeden silinir.\n\nDiskteki dosya "
                    + "kopyaları silinmez; onları ayrıca “Bırakılan dosyaları sil” ile "
                    + "temizlemeniz gerekir."))
        {
            return;
        }

        island.Clear();
    }

    /// <summary>
    /// Geri dönüşsüz bir bakım eylemi için onay ister.
    /// </summary>
    /// <remarks>
    /// Depoda daha önce bir onay kalıbı yoktu. WPF-UI <c>ContentDialog</c> denendi ve
    /// <b>reddedildi</b>: yansımayla ölçüldü — WPF-UI 4.3.0'ın <c>ContentDialog</c>'ında
    /// <c>XamlRoot</c> özelliği yok, tek halka açık yöntemi <c>ShowAsync(CancellationToken)</c>
    /// ve barındırmayı <c>ContentDialogHost.GetForWindow(Window)</c> üzerinden çözüyor; oysa
    /// <c>SettingsWindow</c>'ın içeriği bir <c>NavigationView</c>, <c>ContentDialogHost</c>
    /// değil. Yani kullanılabilir bir yol, ayar penceresinin XAML'ını yapısal olarak
    /// değiştirmeyi gerektiriyor — ve sözleşme uygulamayı çalıştırmayı yasakladığı için
    /// doğrulanamaz. Yanlış bir WPF-UI kontrol adının XAML parse hatasıyla prosesi öldürmesi
    /// bu dalda kabul edilen risk değil; <c>System.Windows.MessageBox</c> (BCL, derleyiciyle
    /// doğrulanır) kullanıldı.
    /// </remarks>
    /// <returns>
    /// Kullanıcı onayladıysa <c>true</c>; iptal, kapatma, pencere bulunamaması ya da
    /// gösterilememesi durumunda <c>false</c> — yani onay alınamadıysa eylem <b>yapılmaz</b>
    /// (fail-closed).
    /// </returns>
    private bool ConfirmDestructive(string title, string message)
    {
        Window? owner = Window.GetWindow(this);
        if (owner is null)
        {
            Debug.WriteLine("[WharfBar] island confirm skipped, no owner window");
            return false;
        }

        try
        {
            return MessageBox.Show(
                       owner,
                       message,
                       title,
                       MessageBoxButton.OKCancel,
                       MessageBoxImage.Warning)
                   == MessageBoxResult.OK;
        }
        catch (Exception ex)
        {
            // Onay gösterilemedi: geri dönüşsüz eylem yapılmaz, ama neden görünmez olmamalı.
            Debug.WriteLine($"[WharfBar] island confirm failed: {ex.Message}");
            return false;
        }
    }

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
