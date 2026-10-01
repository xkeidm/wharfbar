using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Island'ın tüm saf mantığı: kabul kuralı, dedupe, budama, arama eşleştirme, önizleme
/// biçimlendirme ve bırakma dosya adı üretimi. Win32 ve dosya sistemi burada değildir —
/// bu yüzden tamamı birim testlenebilir. Canlı servisler (<c>IslandService</c>,
/// <c>IslandStore</c>) yalnızca çağırır.
/// </summary>
internal static class IslandPruner
{
    /// <summary>Bu boyutu aşan pano içeriği kaydedilmez.</summary>
    internal const int MaxTextBytes = 64 * 1024;

    /// <summary>Pill ve liste önizlemesinde gösterilen karakter sayısı.</summary>
    internal const int PreviewChars = 40;

    /// <summary>Listede kaynak uygulama adının uzunluk sınırı.</summary>
    internal const int SourceAppMaxChars = 24;

    /// <summary>
    /// Yakalanan bir pano içeriğinin geçmişe alınıp alınmayacağı.
    /// </summary>
    /// <param name="historyOptOut">
    /// <c>CanIncludeInClipboardHistory</c> formatı 0 ise <c>true</c> — parola yöneticileri
    /// kopyalarını bu işaretle kendi dışında tutar, biz de tutarız.
    /// </param>
    /// <param name="text">Okunan metin.</param>
    /// <param name="byteCount">Metnin UTF-8 bayt uzunluğu.</param>
    internal static bool ShouldCapture(bool historyOptOut, string? text, int byteCount)
    {
        if (historyOptOut || text is null)
        {
            return false;
        }

        if (byteCount > MaxTextBytes || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Yeni kaydı başa ekler. <paramref name="current"/> en yenisi başta olacak şekilde
    /// sıralı beklenir. Ardışık aynı metin kopyalanmışsa kopya açılmaz, mevcut kayıt
    /// yalnız başa taşınır (Windows geçmişinin davranışı). Sonra budama uygulanır.
    /// </summary>
    internal static IReadOnlyList<IslandEntry> Prepend(
        IReadOnlyList<IslandEntry> current,
        IslandEntry entry,
        int maxItems,
        DateTimeOffset now,
        int retentionDays,
        bool persistent)
    {
        var result = new List<IslandEntry>(current.Count + 1);
        result.Add(entry);

        bool mergedWithFirst = current.Count > 0 && IsSameContent(current[0], entry);
        for (int i = mergedWithFirst ? 1 : 0; i < current.Count; i++)
        {
            result.Add(current[i]);
        }

        return Prune(result, maxItems, now, retentionDays, persistent);
    }

    /// <summary>
    /// Gün limiti + kayıt sayısı tavanını uygular. <paramref name="entries"/> en yenisi başta
    /// olacak şekilde sıralı beklenir: <c>Take</c> baştan kestiği için sıra korunur, kayıtlar
    /// yeniden sıralanmaz. <paramref name="persistent"/> false ise gün limiti yok sayılır
    /// (oturumluk modda eski kayıtlar da tutulur).
    /// </summary>
    /// <remarks>
    /// <paramref name="retentionDays"/> 0 (veya altı) "sınırsız" <b>anlamına gelmez</b>:
    /// ayar yolu (<c>IslandNormalizer.ClampRetentionDays</c>) değeri 1..3650'e sıkıştırdığı
    /// için <c>&gt; 0</c> dalı her zaman alınır; <c>&lt;= 0</c> yalnızca bu metodu doğrudan
    /// çağıran testler için geçerlidir ve gün limitini uygulamaz. Sınırın tam kendisindeki
    /// kayıt (<c>&gt;=</c> karşılaştırması) <b>kalır</b>. <paramref name="maxItems"/> 0 veya
    /// altıysa sonuç boştur — tavan 1'e yükseltilmez.
    /// </remarks>
    internal static IReadOnlyList<IslandEntry> Prune(
        IReadOnlyList<IslandEntry> entries,
        int maxItems,
        DateTimeOffset now,
        int retentionDays,
        bool persistent)
    {
        IEnumerable<IslandEntry> kept = entries;

        if (persistent && retentionDays > 0)
        {
            DateTime cutoff = now.UtcDateTime.AddDays(-retentionDays);
            kept = kept.Where(e => e.CapturedAtUtc >= cutoff);
        }

        return kept.Take(Math.Max(0, maxItems)).ToList();
    }

    /// <summary>Kaydın arama sorgusuyla eşleşip eşleşmediği. Boş sorgu her şeyi eşleştirir.</summary>
    internal static bool Matches(IslandEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string q = query.Trim();
        return Contains(entry.Text, q)
            || Contains(entry.FilePath, q)
            || Contains(entry.SourceApp, q);
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Satır sonlarını ve fazla boşlukları tek satıra indirir, uzun metni kısar.</summary>
    internal static string FormatPreview(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        string single = sb.ToString();
        return single.Length <= PreviewChars ? single : string.Concat(single.AsSpan(0, PreviewChars), "…");
    }

    /// <summary>Listede gösterilen göreli zaman etiketi.</summary>
    internal static string FormatRelativeTime(DateTime capturedAtUtc, DateTimeOffset now)
    {
        TimeSpan delta = now.UtcDateTime - capturedAtUtc;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 1)
        {
            return "şimdi";
        }

        if (delta.TotalHours < 1)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0} dk önce", (int)delta.TotalMinutes);
        }

        if (delta.TotalDays < 1)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0} sa önce", (int)delta.TotalHours);
        }

        return string.Format(CultureInfo.CurrentCulture, "{0} gün önce", (int)delta.TotalDays);
    }

    /// <summary>
    /// Ad çakışması için dosya adı üretir. Kural <b>yalnızca dosya adı</b> üzerinde uygulanır:
    /// önce <c>Path.GetFileName</c> ile yol bileşeni atılır, sonra <paramref name="suffix"/>
    /// 0 ise ad olduğu gibi döner, aksi halde uzantıdan önce " (n)" eklenir (uzantısız adlar
    /// dahil). Dönen değer daima yalın bir dosya adıdır — yol, çağıran taraf
    /// <c>Path.Combine(DropRoot, sonuç)</c> ile kurulur. Böylece nokta içeren bir dizin adı
    /// (<c>my.dir</c>) yol biçimini bozamaz.
    /// </summary>
    internal static string BuildDropFileName(string fileName, int suffix)
    {
        string name = Path.GetFileName(fileName);
        if (suffix <= 0)
        {
            return name;
        }

        string marker = string.Format(CultureInfo.InvariantCulture, " ({0})", suffix);
        int dot = name.LastIndexOf('.');
        return dot > 0
            ? string.Concat(name.AsSpan(0, dot), marker, name.AsSpan(dot))
            : string.Concat(name, marker);
    }

    private static bool IsSameContent(IslandEntry a, IslandEntry b) =>
        a.Kind == b.Kind
        && string.Equals(a.Text, b.Text, StringComparison.Ordinal)
        && string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
}
