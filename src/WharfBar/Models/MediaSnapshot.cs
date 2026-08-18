using System.Windows.Media;

namespace WharfBar.Models;

/// <summary>
/// Çalan medyanın anlık görüntüsü (SMTC oturumu). Saf veri kaydı — unit testli.
/// </summary>
internal sealed record MediaSnapshot(
    string SessionId,
    string AppName,
    string Title,
    string Artist,
    bool IsPlaying,
    ImageSource? Thumbnail)
{
    /// <summary>Oturum yokken kullanılan boş snapshot.</summary>
    internal static MediaSnapshot Idle { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, false, null);

    /// <summary>Aktif SMTC oturumu var mı (id boş değil).</summary>
    internal bool HasSession => SessionId.Length > 0;
}
