namespace WharfBar.Models;

/// <summary>Island kaydının türü: kopyalanan metin ya da island'a bırakılan dosya.</summary>
internal enum IslandEntryKind
{
    /// <summary>Panodan yakalanan metin.</summary>
    Text,

    /// <summary>Island'a sürüklenip kopyalanan dosya.</summary>
    File,
}

/// <summary>
/// Island zaman çizelgesinin tek bir kaydı. <see cref="FilePath"/> yalnız
/// <see cref="IslandEntryKind.File"/> türünde doludur.
/// </summary>
/// <param name="Id">Servis tarafından artan sayıcıdan verilir.</param>
/// <param name="Kind">Kayıt türü.</param>
/// <param name="Text">Görüntülenecek metin (dosyalarda dosya adı).</param>
/// <param name="FilePath">Dosyanın <c>%AppData%\WharfBar\Drop</c> altındaki tam yolu.</param>
/// <param name="CapturedAtUtc">Kaydın yakalandığı an (UTC).</param>
/// <param name="SourceApp">Kaynağın süreç adı; dosya kayıtlarında boştur.</param>
internal sealed record IslandEntry(
    long Id,
    IslandEntryKind Kind,
    string Text,
    string? FilePath,
    DateTime CapturedAtUtc,
    string? SourceApp);
