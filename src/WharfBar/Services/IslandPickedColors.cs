using System.Windows.Media;

namespace WharfBar.Services;

/// <summary>
/// Renk seçicinin "son renkler" deposu. Yalnız oturumluk: diske yazılmaz, ada şemasına
/// girmez — ekran seçicinin her tıklaması buraya düşer, gövde her sunumda yeniden kurar
/// (<c>IslandService.Changed</c> benzeri bildirim yok: listede değişiklik yalnız seçim
/// kapandıktan sonra olur ve seçim güzergâhı gövdeyi zaten yeniden sergiler).
/// Ekleme/budaşma mantığı pure (<see cref="Pushed"/>) — testler doğrudan sınar.
/// </summary>
internal static class IslandPickedColors
{
    /// <summary>Tek seferde tutulan renk tavanı (gövde şeridi için de aynı sayı).</summary>
    internal const int MaxItems = 8;

    private static IReadOnlyList<Color> _colors = [];

    internal static IReadOnlyList<Color> Items => _colors;

    /// <summary>En yeni seçim; hiç seçim yoksa şeffaf (çağıran boş-liste yolunu da sınar).</summary>
    internal static Color Last => _colors.Count > 0 ? _colors[0] : default;

    /// <summary>UI thread'inde çağrılır. Seçimi listeye alınmış gibi listeyi döndürür.</summary>
    internal static void Add(Color color) => _colors = new List<Color>(Pushed(_colors, color, MaxItems));

    /// <summary>
    /// Pure ekleme kuralı: yeni renk başa geçer, birebir aynı renk çoğaltılmaz (başa taşınır),
    /// kapasite aşımında en eskiden başlanarak budaşılır. Şeffaf (bir örneklemin başarısız
    /// olduğu) renk listeye girmez.
    /// </summary>
    internal static IReadOnlyList<Color> Pushed(IReadOnlyList<Color>? existing, Color added, int capacity)
    {
        if (capacity <= 0 || added.A == 0)
        {
            return existing ?? [];
        }

        var result = new List<Color>(Math.Min(capacity, (existing?.Count ?? 0) + 1)) { added };
        if (existing is not null)
        {
            foreach (Color color in existing)
            {
                if (color != added)
                {
                    result.Add(color);
                }
            }
        }

        if (result.Count > capacity)
        {
            result.RemoveRange(capacity, result.Count - capacity);
        }

        return result;
    }
}
