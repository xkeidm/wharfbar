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
    ImageSource? Thumbnail,
    TimeSpan? Position = null,
    TimeSpan? Duration = null,
    bool CanSeek = false)
{
    /// <summary>Oturum yokken kullanılan boş snapshot.</summary>
    internal static MediaSnapshot Idle { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, false, null);

    /// <summary>Aktif SMTC oturumu var mı (id boş değil).</summary>
    internal bool HasSession => SessionId.Length > 0;

    /// <summary>
    /// SMTC konum ve süre ikisi de varsa <c>m:ss / m:ss</c> (saat varsa <c>h:mm:ss</c>).
    /// Biri eksikse veya süre 0 ise null — yüzde uydurulmaz.
    /// </summary>
    internal string? TimelineText()
    {
        if (Position is not TimeSpan position || Duration is not TimeSpan duration || duration <= TimeSpan.Zero)
        {
            return null;
        }

        return $"{FormatClock(position)} / {FormatClock(duration)}";
    }

    private static string FormatClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }
}
