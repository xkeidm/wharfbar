# Island (clipboard geçmişi + bırakılan dosyalar) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WharfBar'ın üst barının ortasına, macOS notch uygulamaları konseptinde bir "Dynamic Island" eklemek: kapalıyken son kopyalananın önizlemesini gösterir, açıldığında clipboard geçmişi ile island'a bırakılan dosyaların tek birleşik zaman çizelgesini sunar.

**Architecture:** Island mevcut `TopBarWindow` XAML'inin **orta sütununa** yerleşen bir `Border`'dır; açılışı `TopBarWindow` içinde bir `Popup` ile yapılır — ikinci bir pencere, yeni z-order düzeni veya yeni odak yönetimi yoktur. Pano yakalama mesaj-only bir `HwndSource` (`WM_CLIPBOARDUPDATE`) ile yapılır, okuma **ayrı bir STA dispatcher thread'inde** gerçekleşir. Tüm saf mantık (budama, dedupe, arama, önizleme biçimlendirme, bırakma yolu) test edilebilir bir `IslandPruner` statik sınıfında toplanır; Win32 pano okuma, sürükle-bırak ve `Popup` animasyonu canlı gözlemle doğrulanır.

**Tech Stack:** .NET 8, WPF, WPF-UI 4.3.0, `System.Management` 8.0.0 (mevcut iki paket). Yeni bağımlılık yok. xUnit 2.9.3 (mevcut test projesi, `UseWPF=true`, `InternalsVisibleTo`).

**Spec:** `docs/superpowers/specs/2026-10-01-island-clipboard-drop-design.md`

## Global Constraints

Bütün görevler bu bölümün gereksinimlerini örtük olarak taşır.

- **Yeni NuGet paketi ekleme.** Proje iki pakete bağlıdır (`WPF-UI`, `System.Management`). `boundsaries.md` "Dış bağımlılık ekleme"yi kullanıcı onayına bağlar; bu plan onay almadı.
- **`AllowsTransparency=true` ile Mica/DWM backdrop birlikte kullanma** (`boundsaries.md`). Bu özellikte `AllowsTransparency` **yalnızca** `Popup` üzerinde kullanılır; ana bar Mica yüzeyinde şeffaflık yoktur.
- **`Popup` içinde iç içe `Popup` kurma** — odak kaçar, liste görünmez (`docs/04-gotchas.md`). Island listesi `ScrollViewer` + `ItemsControl` olmalıdır.
- **WPF-UI `SymbolIcon` enum adını pakette doğrulamadan XAML'e yazma.** Yanlış enum adı XAML parse hatası verip **prosesi öldürür** (`docs/04-gotchas.md` — `Meter24` ile yaşandı). Enum adı doğrulanmadan önce nav ikonu eklenmez.
- **`ConfigService.Save` senkron ve UI thread'inde çalışır.** Pano geçmişi **asla** `config.json`'a yazılmaz; ayrı dosya kullanılır.
- **`AppConfig.CurrentSchemaVersion` = 2 olarak kalır.** Island bloğu **yumuşak** eklenir (ADR-007 deseni: eklenebilir `veil` bloğu gibi). Şema sürümü artırılmaz.
- **Clipboard okuması UI thread'inde yapılmaz.** `OpenClipboard` bloklayabilir; okuma her zaman STA worker'dadır.
- **Dosyalar metadata JSON'una girmez.** Bırakılan dosyalar `%AppData%\WharfBar\Drop\` altında durur.
- **MVVM yok.** Mevcut kalıp: code-behind + doğrudan `x:Name` erişimi, MVVM paketi yok.
- **Dosya adlandırma ve yorum dili:** mevcut kod tabanıyla aynı — `internal` üyeler, doc comment, Türkçe commit mesajı.

---

## File Structure

**Oluşturulacak:**

| Dosya | Tek sorumluluk |
|-------|---------------|
| `src/WharfBar/Models/IslandEntry.cs` | Veri taşıyıcıları: `IslandEntryKind` enum'u + `IslandEntry` record'u |
| `src/WharfBar/Services/IslandPruner.cs` | Tüm saf mantık: kabul kuralı, dedupe, budama, arama, önizleme, göreli zaman, bırakma dosya adı |
| `src/WharfBar/Services/IslandStore.cs` | `island.json` okuma/yazma (atomik), bozuk dosyada `.bak`, `Drop` kök yolu |
| `src/WharfBar/Services/IslandService.cs` | Yaşam döngüsü: STA thread, dinleyici kurulumu, orkestrasyon, debounce yazma, `Dispose` flush |
| `src/WharfBar/Native/ClipboardNative.cs` | Pano P/Invoke'ları (`OpenClipboard`, `GetClipboardData`, `AddClipboardFormatListener`, …) |
| `src/WharfBar/Services/ClipboardMessageWindow.cs` | Mesaj-only `HwndSource` + `WM_CLIPBOARDUPDATE` kancası |
| `src/WharfBar/Views/IslandFlyout.xaml` + `.xaml.cs` | Panel: arama kutusu + liste |
| `src/WharfBar/Views/Pages/IslandPage.xaml` + `.xaml.cs` | Ayar sayfası |
| `tests/WharfBar.Tests/IslandPrunerTests.cs` | Saf çekirdeğin testleri |
| `tests/WharfBar.Tests/IslandConfigTests.cs` | `IslandNormalizer` sınırları |
| `tests/WharfBar.Tests/IslandStoreTests.cs` | Store okuma/yazma/bozuk dosya |

**Değiştirilecek:**

| Dosya | Değişiklik |
|-------|-----------|
| `src/WharfBar/Models/AppConfig.cs` | `IslandConfig` + `IslandNormalizer` + `AppConfig.Island` |
| `src/WharfBar/Services/ConfigService.cs` | `Normalize()` bağlama, `CreateDefault()`, `island` anahtarı eksikse kaydetme |
| `src/WharfBar/Services/TopBarEngine.cs` | `Island` alanı + `Dispose` |
| `src/WharfBar/Views/TopBarWindow.xaml` | `IslandPill` + `IslandPopup` |
| `src/WharfBar/Views/TopBarWindow.xaml.cs` | Durum makinesi, `ToggleIsland`, `CloseFlyouts` zinciri, sürükle-bırak |
| `src/WharfBar/Views/SettingsWindow.xaml` | Nav girdisi |
| `src/WharfBar/App.xaml.cs` | Servis kurulumu + `OnExit` dispose |
| `docs/01-architecture.md`, `docs/04-gotchas.md`, `.agent/*`, `docs/decisions/010-*.md` | Dokümantasyon |

---

## Task 1: `IslandPruner` — saf çekirdek

Tüm mantığın dayandığı taş. Bu görev tamamen test edilebilir; sonraki tüm görevler buradaki imzaları tüketir.

**Files:**
- Create: `src/WharfBar/Models/IslandEntry.cs`
- Create: `src/WharfBar/Services/IslandPruner.cs`
- Test: `tests/WharfBar.Tests/IslandPrunerTests.cs`

**Interfaces:**
- Consumes: (yok — bağımlılığı yok)
- Produces:
  - `namespace WharfBar.Models`: `internal enum IslandEntryKind { Text, File }`, `internal sealed record IslandEntry(long Id, IslandEntryKind Kind, string Text, string? FilePath, DateTime CapturedAtUtc, string? SourceApp)`
  - `namespace WharfBar.Services` → `internal static class IslandPruner`:
    - `const int MaxTextBytes = 64 * 1024`
    - `const int PreviewChars = 40`
    - `const int SourceAppMaxChars = 24`
    - `static bool ShouldCapture(bool historyOptOut, string? text, int byteCount)`
    - `static IReadOnlyList<IslandEntry> Prepend(IReadOnlyList<IslandEntry> current, IslandEntry entry, int maxItems, DateTimeOffset now, int retentionDays, bool persistent)`
    - `static IReadOnlyList<IslandEntry> Prune(IReadOnlyList<IslandEntry> entries, int maxItems, DateTimeOffset now, int retentionDays, bool persistent)`
    - `static bool Matches(IslandEntry entry, string? query)`
    - `static string FormatPreview(string text)`
    - `static string BuildDropFileName(string fileName, int suffix)`
    - `static string FormatRelativeTime(DateTime capturedAtUtc, DateTimeOffset now)`

- [ ] **Step 1: Test dosyasını ve kayan testleri yaz**

`tests/WharfBar.Tests/IslandPrunerTests.cs` olarak oluştur:

```csharp
using System;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandPrunerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;

    private static IslandEntry Text(string text, DateTime? at = null, string? app = null) =>
        new(1, IslandEntryKind.Text, text, null, at ?? NowUtc, app);

    private static IslandEntry File(string path, DateTime? at = null) =>
        new(2, IslandEntryKind.File, System.IO.Path.GetFileName(path), path, at ?? NowUtc, null);

    [Fact]
    public void ShouldCapture_Rejects_HistoryOptOut()
    {
        Assert.False(IslandPruner.ShouldCapture(historyOptOut: true, "hello", 5));
    }

    [Fact]
    public void ShouldCapture_Rejects_Empty_And_Whitespace()
    {
        Assert.False(IslandPruner.ShouldCapture(false, null, 0));
        Assert.False(IslandPruner.ShouldCapture(false, string.Empty, 0));
        Assert.False(IslandPruner.ShouldCapture(false, "   \t\n  ", 8));
    }

    [Fact]
    public void ShouldCapture_Rejects_Oversized()
    {
        Assert.False(IslandPruner.ShouldCapture(false, "x", IslandPruner.MaxTextBytes + 1));
    }

    [Fact]
    public void ShouldCapture_Accepts_Normal()
    {
        Assert.True(IslandPruner.ShouldCapture(false, "hello", 5));
        Assert.True(IslandPruner.ShouldCapture(false, "x", IslandPruner.MaxTextBytes));
    }

    [Fact]
    public void Prepend_Moves_Consecutive_Duplicate_ToTop_WithoutDuplicating()
    {
        IslandEntry a = Text("aaa", NowUtc.AddMinutes(-5));
        IslandEntry b = Text("bbb", NowUtc.AddMinutes(-1));
        IslandEntry again = Text("aaa", NowUtc);

        IReadOnlyList<IslandEntry> result = IslandPruner.Prepend(
            [a, b], again, 200, Now, 30, persistent: true);

        Assert.Equal(2, result.Count);
        Assert.Equal("aaa", result[0].Text);
        Assert.Equal(NowUtc, result[0].CapturedAtUtc);
        Assert.Equal("bbb", result[1].Text);
    }

    [Fact]
    public void Prepend_Keeps_NonConsecutive_Duplicates()
    {
        IslandEntry a1 = Text("dup", NowUtc.AddMinutes(-9));
        IslandEntry b = Text("other", NowUtc.AddMinutes(-5));
        IslandEntry a2 = Text("dup", NowUtc);

        IReadOnlyList<IslandEntry> result = IslandPruner.Prepend(
            [a1, b], a2, 200, Now, 30, persistent: true);

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Count(e => e.Text == "dup"));
    }

    [Fact]
    public void Prune_Drops_Entries_OlderThan_RetentionDays()
    {
        IslandEntry fresh = Text("fresh", NowUtc.AddHours(-1));
        IslandEntry old = Text("old", NowUtc.AddDays(-31));

        IReadOnlyList<IslandEntry> result = IslandPruner.Prune(
            [fresh, old], 200, Now, retentionDays: 30, persistent: true);

        Assert.Single(result);
        Assert.Equal("fresh", result[0].Text);
    }

    [Fact]
    public void Prune_Ignores_RetentionDays_WhenSession()
    {
        IslandEntry veryOld = Text("ancient", NowUtc.AddDays(-900));

        IReadOnlyList<IslandEntry> result = IslandPruner.Prune(
            [veryOld], 200, Now, retentionDays: 30, persistent: false);

        Assert.Single(result);
    }

    [Fact]
    public void Prune_Applies_MaxItems_HardCap()
    {
        var many = new System.Collections.Generic.List<IslandEntry>();
        for (int i = 0; i < 50; i++)
        {
            many.Add(Text($"e{i}", NowUtc.AddSeconds(-i)));
        }

        IReadOnlyList<IslandEntry> result = IslandPruner.Prune(many, 10, Now, 30, persistent: true);

        Assert.Equal(10, result.Count);
        Assert.Equal("e0", result[0].Text);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Hello", true)]
    [InlineData("hello", true)]
    [InlineData("ELL", true)]
    [InlineData("zzz", false)]
    public void Matches_IsCaseInsensitive_AndTreatsBlankAsMatchAll(string? query, bool expected)
    {
        Assert.Equal(expected, IslandPruner.Matches(Text("Hello"), query));
    }

    [Fact]
    public void Matches_Searches_FileName_And_SourceApp()
    {
        Assert.True(IslandPruner.Matches(File(@"C:\x\rapor.pdf"), "rapor"));
        Assert.True(IslandPruner.Matches(Text("kaynak", app: "Notepad"), "notepad"));
    }

    [Fact]
    public void FormatPreview_CollapsesWhitespace_AndTruncates()
    {
        Assert.Equal("tek satır", IslandPruner.FormatPreview("tek\r\nsatır"));
        Assert.Equal("a b c", IslandPruner.FormatPreview("  a   b \t c  "));

        string long_ = new('x', 100);
        string result = IslandPruner.FormatPreview(long_);
        Assert.Equal(IslandPruner.PreviewChars + 1, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void FormatPreview_KeepsShortTextIntact()
    {
        Assert.Equal("kısa", IslandPruner.FormatPreview("kısa"));
    }

    [Fact]
    public void FormatRelativeTime_Formats_MinutesHoursDays()
    {
        Assert.Equal("şimdi", IslandPruner.FormatRelativeTime(NowUtc, Now));
        Assert.Equal("3 dk önce", IslandPruner.FormatRelativeTime(NowUtc.AddMinutes(-3), Now));
        Assert.Equal("5 sa önce", IslandPruner.FormatRelativeTime(NowUtc.AddHours(-5), Now));
        Assert.Equal("2 gün önce", IslandPruner.FormatRelativeTime(NowUtc.AddDays(-2), Now));
    }

    [Theory]
    [InlineData("rapor.pdf", 0, "rapor.pdf")]
    [InlineData("rapor.pdf", 2, "rapor (2).pdf")]
    [InlineData("arsiv.tar.gz", 3, "arsiv.tar (3).gz")]
    [InlineData("noext", 1, "noext (1)")]
    public void BuildDropFileName_AppendsSuffix_BeforeExtension(string input, int suffix, string expected)
    {
        Assert.Equal(expected, IslandPruner.BuildDropFileName(input, suffix));
    }
}
```

- [ ] **Step 2: Testi çalıştır, derlenmediğini doğrula**

Run: `dotnet test WharfBar.sln --nologo -v quiet`
Expected: FAIL — `CS0246: 'WharfBar.Models' / 'IslandPruner' bulunamadı`.

- [ ] **Step 3: `IslandEntry.cs` oluştur**

`src/WharfBar/Models/IslandEntry.cs`:

```csharp
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
```

- [ ] **Step 4: `IslandPruner.cs` oluştur**

`src/WharfBar/Services/IslandPruner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Island'ın tüm saf mantığı: kabul kuralı, dedupe, budama, arama eşleştirme, önizleme
/// biçimlendirme ve bırakma dosya adı üretimi. Win32 ve dosya sistemi burada değildir —
/// bu yüzden tamamı birim testlenebilir. Canlı servisler (<see cref="IslandService"/>,
/// <see cref="IslandStore"/>) yalnızca çağırır.
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
    /// Yeni kaydı başa ekler. Ardışık aynı metin kopyalanmışsa kopya açılmaz, mevcut kayıt
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
        bool mergedWithFirst = false;

        if (current.Count > 0 && IsSameContent(current[0], entry))
        {
            result.Add(entry);
            mergedWithFirst = true;
        }
        else
        {
            result.Add(entry);
        }

        for (int i = mergedWithFirst ? 1 : 0; i < current.Count; i++)
        {
            result.Add(current[i]);
        }

        return Prune(result, maxItems, now, retentionDays, persistent);
    }

    /// <summary>
    /// Gün limiti + kayıt sayısı tavanını uygular. <paramref name="persistent"/> false ise
    /// gün limiti yok sayılır (oturumluk modda eski kayıtlar da tutulur).
    /// </summary>
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

        return kept.Take(Math.Max(1, maxItems)).ToList();
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
    /// Ad çakışması için dosya adı üretir. <paramref name="suffix"/> 0 ise ad değişmez;
    /// aksi halde uzantıdan önce " (n)" eklenir (klasörler ve uzantısız adlar dahil).
    /// </summary>
    internal static string BuildDropFileName(string fileName, int suffix)
    {
        if (suffix <= 0)
        {
            return fileName;
        }

        string marker = string.Format(CultureInfo.InvariantCulture, " ({0})", suffix);
        int dot = fileName.LastIndexOf('.');
        return dot > 0
            ? string.Concat(fileName.AsSpan(0, dot), marker, fileName.AsSpan(dot))
            : string.Concat(fileName, marker);
    }

    private static bool IsSameContent(IslandEntry a, IslandEntry b) =>
        a.Kind == b.Kind
        && string.Equals(a.Text, b.Text, StringComparison.Ordinal)
        && string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 5: Testleri çalıştır, geçtiğini doğrula**

Run: `dotnet test WharfBar.sln --nologo -v quiet`
Expected: `Başarılı! - Başarısız: 0, Başarılı: 337` (mevcut 320 + bu dosyadaki 17).

- [ ] **Step 6: Commit**

```bash
git add src/WharfBar/Models/IslandEntry.cs src/WharfBar/Services/IslandPruner.cs tests/WharfBar.Tests/IslandPrunerTests.cs
git commit -m "feat(island): saf cekirdek - dedupe, budama, arama ve onizleme"
```

---

## Task 2: `IslandConfig` ve config entegrasyonu

**Files:**
- Modify: `src/WharfBar/Models/AppConfig.cs` (satır 32 `Veil` özelliğinin ardına)
- Modify: `src/WharfBar/Services/ConfigService.cs` (satır 55-56, 84, 216-218)
- Test: `tests/WharfBar.Tests/IslandConfigTests.cs`

**Interfaces:**
- Consumes: `WharfBar.Models.IslandEntry` (Task 1) — bu görevde kullanılmaz, sadece aynı assembly'de durur.
- Produces:
  - `namespace WharfBar` → `internal sealed class IslandConfig`:
    - `const bool DefaultEnabled = true`, `const int DefaultRetentionDays = 30`, `const int DefaultMaxItems = 200`
    - `enum PersistenceMode { Session, Persistent }` (iç içe)
    - `bool Enabled`, `PersistenceMode Persistence`, `int RetentionDays`, `int MaxItems`
  - `namespace WharfBar` → `internal static class IslandNormalizer`:
    - `static int ClampRetentionDays(int value)` → `Math.Clamp(value, 1, 3650)`
    - `static int ClampMaxItems(int value)` → `Math.Clamp(value, 10, 5000)`
  - `AppConfig.Island` özelliği (tür `IslandConfig`, varsayılan `new()`)

- [ ] **Step 1: Testleri yaz**

`tests/WharfBar.Tests/IslandConfigTests.cs`:

```csharp
using WharfBar;
using Xunit;

namespace WharfBar.Tests;

public class IslandConfigTests
{
    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(3650, 3650)]
    [InlineData(99999, 3650)]
    public void ClampRetentionDays_StaysInRange(int input, int expected)
    {
        Assert.Equal(expected, IslandNormalizer.ClampRetentionDays(input));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(9, 10)]
    [InlineData(200, 200)]
    [InlineData(5000, 5000)]
    [InlineData(99999, 5000)]
    public void ClampMaxItems_StaysInRange(int input, int expected)
    {
        Assert.Equal(expected, IslandNormalizer.ClampMaxItems(input));
    }

    [Fact]
    public void Defaults_MatchSpec()
    {
        var config = new IslandConfig();
        Assert.True(config.Enabled);
        Assert.Equal(IslandConfig.PersistenceMode.Session, config.Persistence);
        Assert.Equal(IslandConfig.DefaultRetentionDays, config.RetentionDays);
        Assert.Equal(IslandConfig.DefaultMaxItems, config.MaxItems);
    }
}
```

- [ ] **Step 2: Testi çalıştır, derlenmediğini doğrula**

Run: `dotnet test WharfBar.sln --nologo -v quiet`
Expected: FAIL — `CS0103: 'IslandNormalizer' bulunamadı`.

- [ ] **Step 3: `AppConfig.cs`'e `IslandConfig` ve `IslandNormalizer` ekle**

`src/WharfBar/Models/AppConfig.cs` içinde, `public VeilConfig Veil { get; set; } = new();` satırının hemen altına ekle:

```csharp
    public IslandConfig Island { get; set; } = new();
```

Dosyanın sonuna ekle:

```csharp
/// <summary>Island (clipboard geçmişi + bırakılan dosyalar) ayarları.</summary>
internal sealed class IslandConfig
{
    internal const bool DefaultEnabled = true;

    internal const int DefaultRetentionDays = 30;

    internal const int DefaultMaxItems = 200;

    /// <summary>Geçmişin nerede tutulacağı.</summary>
    internal enum PersistenceMode
    {
        /// <summary>Yalnız bellekte; WharfBar kapanınca silinir. Varsayılan.</summary>
        Session,

        /// <summary>Diske yazılır; <c>RetentionDays</c> ile budanır.</summary>
        Persistent,
    }

    public bool Enabled { get; set; } = DefaultEnabled;

    public PersistenceMode Persistence { get; set; } = PersistenceMode.Session;

    /// <summary>Persistent modda kaç gün geriye bakılır.</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>Kayıt sayısı sert tavanı (gün limitinden bağımsız).</summary>
    public int MaxItems { get; set; } = DefaultMaxItems;
}

/// <summary>Pure static normalization helpers for <see cref="IslandConfig"/> (unit-tested).</summary>
internal static class IslandNormalizer
{
    internal static int ClampRetentionDays(int value) => Math.Clamp(value, 1, 3650);

    internal static int ClampMaxItems(int value) => Math.Clamp(value, 10, 5000);
}
```

- [ ] **Step 4: `ConfigService.Normalize` içine bağla**

`src/WharfBar/Services/ConfigService.cs` içinde, `config.Veil.Color = VeilNormalizer.NormalizeColor(config.Veil.Color);` satırının altına ekle:

```csharp
    config.Island ??= new IslandConfig();
    config.Island.RetentionDays = IslandNormalizer.ClampRetentionDays(config.Island.RetentionDays);
    config.Island.MaxItems = IslandNormalizer.ClampMaxItems(config.Island.MaxItems);
```

- [ ] **Step 5: Eksik anahtarı tespit edip kaydetme zincirine ekle**

`ConfigService.cs` içinde `bool veilKeyMissing = !JsonRootHasProperty(json, "veil");` satırının altına ekle:

```csharp
            bool islandKeyMissing = !JsonRootHasProperty(json, "island");
```

`if (iconChanged || statusChanged || catalogChanged || veilKeyMissing || monitorsChanged)` satırını şu hale getir:

```csharp
            if (iconChanged || statusChanged || catalogChanged || veilKeyMissing
                || islandKeyMissing || monitorsChanged)
```

- [ ] **Step 6: Testleri çalıştır, geçtiğini doğrula**

Run: `dotnet test WharfBar.sln --nologo -v quiet`
Expected: `Başarılı! - Başarısız: 0, Başarılı: 345`.

- [ ] **Step 7: Commit**

```bash
git add src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/ConfigService.cs tests/WharfBar.Tests/IslandConfigTests.cs
git commit -m "feat(island): config blogu - kalicilik, gun limiti, kayit tavani"
```

---

## Task 3: Pano P/Invoke'ları ve mesaj-only dinleyici

**Files:**
- Create: `src/WharfBar/Native/ClipboardNative.cs`
- Create: `src/WharfBar/Services/ClipboardMessageWindow.cs`

**Interfaces:**
- Consumes: `WharfBar.Services.IslandPruner.ShouldCapture` (Task 1)
- Produces:
  - `namespace WharfBar.Native` → `internal static class ClipboardNative`:
    - `const int WM_CLIPBOARDUPDATE = 0x031D`
    - `static bool AddListener(IntPtr hwnd)` / `static void RemoveListener(IntPtr hwnd)`
    - `static bool TryReadText(out string? text, out bool historyOptOut, out int byteCount)`
    - `static bool TryWriteText(string text)`
    - `static string? GetOwnerProcessName()`
  - `namespace WharfBar.Services` → `internal sealed class ClipboardMessageWindow : IDisposable`:
    - `ctor()` — mesaj-only `HwndSource` açar, dinleyiciyi kaydeder
    - `event EventHandler? ClipboardChanged`
    - `void Dispose()`

- [ ] **Step 1: `ClipboardNative.cs` oluştur**

`src/WharfBar/Native/ClipboardNative.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WharfBar.Native;

/// <summary>
/// Belgelenmiş pano P/Invoke'ları. Depoda <c>System.Windows.Clipboard</c> kullanılmaz:
/// UI thread'ine bağlıdır ve <c>OpenClipboard</c> bloklandığında arayüzü dondurur. Buradaki
/// çağrılar STA worker thread'inden yapılır.
/// </summary>
internal static class ClipboardNative
{
    /// <summary>Windows'un "bu içeriği geçmişe alma" opt-out sinyali.</summary>
    private const string CanIncludeInClipboardHistoryFormat = "CanIncludeInClipboardHistory";

    internal const int WM_CLIPBOARDUPDATE = 0x031D;

    private const ushort CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, IntPtr bytes);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr mem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr mem);

    [DllImport("user32.dll")]
    private static extern IntPtr GlobalFree(IntPtr mem);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int MultiByteToWideChar(
        uint codePage, uint flags, byte[]? multiByte, int multiByteCount, StringBuilder? wideChar, int wideCharCount);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int WideCharToMultiByte(
        uint codePage, uint flags, string wideChar, int wideCharCount, byte[]? multiByte, int multiByteCount, IntPtr defaultChar, IntPtr usedDefaultChar);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>Mesaj-only pencereye pano değişim dinleyicisini ekler.</summary>
    internal static bool AddListener(IntPtr hwnd) => hwnd != IntPtr.Zero && AddClipboardFormatListener(hwnd);

    /// <summary>Dinleyiciyi kaldırır (idempotent olmayan Win32 çağrısı — yalnız bir kez çağrılır).</summary>
    internal static void RemoveListener(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            _ = RemoveClipboardFormatListener(hwnd);
        }
    }

    /// <summary>
    /// Panodaki metni okur. <paramref name="historyOptOut"/> true ise kaynak uygulama
    /// içeriği geçmişe almak istemiyor demektir ve kaydedilmemelidir.
    /// </summary>
    internal static bool TryReadText(out string? text, out bool historyOptOut, out int byteCount)
    {
        text = null;
        historyOptOut = false;
        byteCount = 0;

        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            uint optOutId = RegisterFormatId(CanIncludeInClipboardHistoryFormat);
            if (optOutId != 0
                && IsClipboardFormatAvailable(optOutId)
                && GetClipboardData(optOutId) != IntPtr.Zero
                && ReadDword(GetClipboardData(optOutId)) == 0)
            {
                historyOptOut = true;
                return false;
            }

            if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                return false;
            }

            IntPtr handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            IntPtr block = GlobalLock(handle);
            if (block == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(block) ?? string.Empty;
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }

            byteCount = Utf8ByteCount(text);
            return true;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <summary>Metni panoya yazar (yapıştırma yapılmaz — çağıran uygulamaya bırakılır).</summary>
    internal static bool TryWriteText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            int byteCount = Utf8ByteCount(text) + 1;
            IntPtr mem = GlobalAlloc(GMEM_MOVEABLE, new IntPtr(byteCount));
            if (mem == IntPtr.Zero)
            {
                return false;
            }

            IntPtr block = GlobalLock(mem);
            if (block == IntPtr.Zero)
            {
                _ = GlobalFree(mem);
                return false;
            }

            try
            {
                var utf8 = new byte[byteCount];
                int written = WideCharToMultiByte(65001, 0, text, text.Length, utf8, byteCount, IntPtr.Zero, IntPtr.Zero);
                if (written <= 0)
                {
                    return false;
                }

                utf8[written] = 0;
                Marshal.Copy(utf8, 0, block, byteCount);
            }
            finally
            {
                _ = GlobalUnlock(mem);
            }

            if (!EmptyClipboard())
            {
                _ = GlobalFree(mem);
                return false;
            }

            // SetClipboardData takes ownership on success; it must NOT be freed here.
            return SetClipboardData(CF_UNICODETEXT, mem) != IntPtr.Zero;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <summary>Pano sahibi sürecin adı; sahiplik yoksa veya erişilemiyorsa null.</summary>
    internal static string? GetOwnerProcessName()
    {
        IntPtr owner = GetClipboardOwner();
        if (owner == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(owner, out uint pid);
        if (pid == 0)
        {
            return null;
        }

        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            int size = 1024;
            var name = new StringBuilder(size);
            return QueryFullProcessImageName(process, 0, name, ref size)
                ? Path.GetFileNameWithoutExtension(name.ToString())
                : null;
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    private static uint _historyFormatId;

    private static uint RegisterFormatId(string name)
    {
        if (_historyFormatId == 0)
        {
            _historyFormatId = (uint)RegisterClipboardFormatW(name);
        }

        return _historyFormatId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string format);

    private static int ReadDword(IntPtr handle)
    {
        IntPtr block = GlobalLock(handle);
        if (block == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            return Marshal.ReadInt32(block);
        }
        finally
        {
            _ = GlobalUnlock(handle);
        }
    }

    private static int Utf8ByteCount(string? text) =>
        string.IsNullOrEmpty(text)
            ? 0
            : MultiByteToWideChar(0, 0, Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetByteCount(text), null, 0) == 0
                ? Encoding.UTF8.GetByteCount(text)
                : Encoding.UTF8.GetByteCount(text);
}
```

**Not:** `Path` kullanıldığı için dosyanın başına `using System.IO;` ekle. `Utf8ByteCount` yukarıdaki hâliyle sadeleştirilebilir — `Encoding.UTF8.GetByteCount(text)` yeterlidir; yukarıdaki sürümdeki `MultiByteToWideChar` çağrısı gereksizdir ve **kaldırılmalıdır**. Doğru gövde şudur:

```csharp
    private static int Utf8ByteCount(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
```

- [ ] **Step 2: `ClipboardMessageWindow.cs` oluştur**

`src/WharfBar/Services/ClipboardMessageWindow.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Windows.Interop;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Pano değişimlerini dinleyen mesaj-only pencere. Mevcut <see cref="MonitorMessageWindow"/>
/// kalıbının aynısı: masaüstünde görünmez, yalnız mesaj alır. Pencere UI thread'inde
/// açılır; dinleyici <c>WM_CLIPBOARDUPDATE</c>'i orada alır, okuma işi çağıranın sorumluluğundadır.
/// </summary>
internal sealed class ClipboardMessageWindow : IDisposable
{
    private HwndSource? _source;
    private bool _disposed;

    /// <summary>Klipboard içeriği değişti. UI thread'inde tetiklenir.</summary>
    internal event EventHandler? ClipboardChanged;

    internal ClipboardMessageWindow()
    {
        var parameters = new HwndSourceParameters("WharfBarClipboard")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            // Message-only window: no desktop presence, receives WM_CLIPBOARDUPDATE.
            ParentWindow = new IntPtr(-3),
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        ClipboardNative.AddListener(_source.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == ClipboardNative.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            try
            {
                ClipboardChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WharfBar] clipboard change handler failed: {ex.Message}");
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_source is not null)
        {
            ClipboardNative.RemoveListener(_source.Handle);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
```

- [ ] **Step 3: Derle**

Run: `dotnet build WharfBar.sln -v quiet --nologo`
Expected: `0 Hata`.

`Utf8ByteCount`'ın sadeleştirilmiş hâli ile derleniyorsa `MultiByteToWideChar` bildirimi kullanılmadığı için CS0169 uyarısı verebilir — bu durumda o P/Invoke bildirimini de sil.

- [ ] **Step 4: Commit**

```bash
git add src/WharfBar/Native/ClipboardNative.cs src/WharfBar/Services/ClipboardMessageWindow.cs
git commit -m "feat(island): pano P/Invoke'lari ve mesaj-only degisim dinleyicisi"
```

---

## Task 4: `IslandStore` — kalıcılık

**Files:**
- Create: `src/WharfBar/Services/IslandStore.cs`
- Test: `tests/WharfBar.Tests/IslandStoreTests.cs`

**Interfaces:**
- Consumes: `WharfBar.Models.IslandEntry` (Task 1), `WharfBar.Services.IslandPruner` (Task 1)
- Produces: `namespace WharfBar.Services` → `internal sealed class IslandStore`:
  - `ctor(string appDataRoot)`
  - `string StorePath { get; }`, `string DropRoot { get; }`
  - `IReadOnlyList<IslandEntry> Load()`
  - `void Save(IReadOnlyList<IslandEntry> entries)`

- [ ] **Step 1: Testleri yaz**

`tests/WharfBar.Tests/IslandStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wharfbar-island-tests", Guid.NewGuid().ToString("N"));

    private IslandStore NewStore() => new(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void StorePath_And_DropRoot_LiveUnderAppDataRoot()
    {
        IslandStore store = NewStore();
        Assert.Equal(Path.Combine(_root, "island.json"), store.StorePath);
        Assert.Equal(Path.Combine(_root, "Drop"), store.DropRoot);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(NewStore().Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEntries()
    {
        IslandStore store = NewStore();
        var entries = new[]
        {
            new IslandEntry(1, IslandEntryKind.Text, "merhaba", null, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), "Notepad"),
            new IslandEntry(2, IslandEntryKind.File, "a.pdf", @"C:\Drop\a.pdf", new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc), null),
        };

        store.Save(entries);
        var loaded = NewStore().Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("merhaba", loaded[0].Text);
        Assert.Equal("Notepad", loaded[0].SourceApp);
        Assert.Equal(IslandEntryKind.File, loaded[1].Kind);
        Assert.Equal(@"C:\Drop\a.pdf", loaded[1].FilePath);
    }

    [Fact]
    public void Load_CorruptFile_BacksUpAndReturnsEmpty()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);
        File.WriteAllText(store.StorePath, "{ this is not json");

        Assert.Empty(store.Load());
        Assert.True(File.Exists(store.StorePath + ".bak"), "bozuk dosya .bak olarak korunmalı");
    }

    [Fact]
    public void Load_PrunesExpiredEntries_WhenPersistent()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);
        var payload = new
        {
            entries = new object[]
            {
                new { id = 1, kind = "Text", text = "eski", filePath = (string?)null, capturedAtUtc = DateTime.UtcNow.AddDays(-40), sourceApp = (string?)null },
                new { id = 2, kind = "Text", text = "yeni", filePath = (string?)null, capturedAtUtc = DateTime.UtcNow, sourceApp = (string?)null },
            },
        };
        File.WriteAllText(store.StorePath, JsonSerializer.Serialize(payload));

        var loaded = store.Load();

        Assert.Single(loaded);
        Assert.Equal("yeni", loaded[0].Text);
    }
}
```

- [ ] **Step 2: Testi çalıştır, derlenmediğini doğrula**

Run: `dotnet test WharfBar.sln --nologo -v quiet`
Expected: FAIL — `CS0246: 'IslandStore' bulunamadı`.

- [ ] **Step 3: `IslandStore.cs` oluştur**

`src/WharfBar/Services/IslandStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Island geçmişinin kalıcı deposu. <c>config.json</c> kullanılmaz: <c>ConfigService.Save</c>
/// her çağrıda dosyanın tamamını UI thread'inde yeniden yazar, pano değişiklikleri ise
/// yüksek frekanslıdır. Yazma atomiktir (tmp + <c>File.Replace</c>), bozuk dosya
/// <c>.bak</c> olarak korunur ve açılış asla kilitlenmez.
/// </summary>
internal sealed class IslandStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal IslandStore(string appDataRoot)
    {
        StorePath = Path.Combine(appDataRoot, "island.json");
        DropRoot = Path.Combine(appDataRoot, "Drop");
    }

    internal string StorePath { get; }

    internal string DropRoot { get; }

    /// <summary>Zaman damgası alanını UTC olarak yorumlayan kayıt sarmalayıcısı.</summary>
    private sealed class EntryDocument
    {
        public long Id { get; set; }

        public IslandEntryKind Kind { get; set; }

        public string Text { get; set; } = string.Empty;

        public string? FilePath { get; set; }

        public DateTime CapturedAtUtc { get; set; }

        public string? SourceApp { get; set; }
    }

    private sealed class StoreDocument
    {
        public int Version { get; set; } = 1;

        public List<EntryDocument> Entries { get; set; } = [];
    }

    /// <summary>
    /// Geçmişi okur. Dosya yoksa boş liste döner; bozuksa <c>.bak</c> alır ve boş döner.
    /// Yüklenen kayıtlar 30 günlük varsayılan budama ile temizlenir (eski bir dosya elle
    /// düzenlendiyse sınırsız kayıt birikmesin).
    /// </summary>
    internal IReadOnlyList<IslandEntry> Load()
    {
        if (!File.Exists(StorePath))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(StorePath);
            var doc = JsonSerializer.Deserialize<StoreDocument>(json, JsonOptions);
            if (doc?.Entries is null)
            {
                return [];
            }

            var entries = new List<IslandEntry>(doc.Entries.Count);
            foreach (EntryDocument e in doc.Entries)
            {
                entries.Add(new IslandEntry(
                    e.Id,
                    e.Kind,
                    e.Text ?? string.Empty,
                    e.FilePath,
                    DateTime.SpecifyKind(e.CapturedAtUtc, DateTimeKind.Utc),
                    e.SourceApp));
            }

            return IslandPruner.Prune(
                entries,
                maxItems: int.MaxValue,
                now: DateTimeOffset.UtcNow,
                retentionDays: 30,
                persistent: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            BackupCorrupt();
            return [];
        }
    }

    /// <summary>Geçmişi atomik olarak yazar. Hata çağırana yansımaz (arka plan yazma best-effort).</summary>
    internal void Save(IReadOnlyList<IslandEntry> entries)
    {
        try
        {
            string? dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var doc = new StoreDocument();
            foreach (IslandEntry e in entries)
            {
                doc.Entries.Add(new EntryDocument
                {
                    Id = e.Id,
                    Kind = e.Kind,
                    Text = e.Text,
                    FilePath = e.FilePath,
                    CapturedAtUtc = e.CapturedAtUtc,
                    SourceApp = e.SourceApp,
                });
            }

            string tmp = StorePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOptions));
            File.Move(tmp, StorePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[WharfBar] island save failed: {ex.Message}");
        }
    }

    private void BackupCorrupt()
    {
        try
        {
            File.Move(StorePath, StorePath + ".bak", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[WharfBar] island backup failed: {ex.Message}");
        }
    }
}
```

- [ ] **Step 4: Testleri çalıştır, geçtiğini doğrula**

Run: `dotnet test WharfBar.sln --nologo -v quiet`
Expected: `Başarılı! - Başarısız: 0, Başarılı: 350`.

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Services/IslandStore.cs tests/WharfBar.Tests/IslandStoreTests.cs
git commit -m "feat(island): atomik kalici depo, bozuk dosyada .bak"
```

---

## Task 5: `IslandService` — orkestrasyon ve STA thread

**Files:**
- Create: `src/WharfBar/Services/IslandService.cs`
- Modify: `src/WharfBar/Services/TopBarEngine.cs`

**Interfaces:**
- Consumes: `IslandEntry`/`IslandPruner` (Task 1), `IslandStore` (Task 4), `IslandConfig`/`IslandNormalizer` (Task 2), `ClipboardNative`/`ClipboardMessageWindow` (Task 3)
- Produces: `namespace WharfBar.Services` → `internal sealed class IslandService : IDisposable`:
  - `ctor(System.Windows.Threading.Dispatcher dispatcher, IslandStore store, IslandConfig config)`
  - `IslandConfig Config { get; }`
  - `IReadOnlyList<IslandEntry> Entries { get; }`
  - `IslandEntry? Latest { get; }`
  - `event EventHandler? Changed`
  - `void ApplyConfig(IslandConfig config)`
  - `void AddDroppedFiles(IReadOnlyList<string> sourcePaths)`
  - `void PutBack(IslandEntry entry)`
  - `void Remove(IslandEntry entry)`
  - `void Clear()`
  - `void DeleteDroppedFiles()`
  - `void Dispose()`

- [ ] **Step 1: `IslandService.cs` oluştur**

`src/WharfBar/Services/IslandService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Island'ın yaşam döngüsü: pano dinleyicisi, STA okuma thread'i, tek zaman çizelgesi ve
/// debounce'lu diske yazma. Saf mantık <see cref="IslandPruner"/>'da; burada yalnızca
/// Win32/disk/thread yönetimi ve orkestrasyon vardır.
/// </summary>
internal sealed class IslandService : IDisposable
{
    private const int SaveDebounceMs = 750;
    private const int OpenClipboardRetries = 3;
    private const int OpenClipboardRetryDelayMs = 40;

    private readonly Dispatcher _dispatcher;
    private readonly IslandStore _store;
    private readonly List<IslandEntry> _entries = [];
    private Dispatcher? _staDispatcher;
    private ClipboardMessageWindow? _listener;
    private System.Threading.Timer? _saveTimer;
    private long _nextId;
    private bool _disposed;
    private bool _savePending;

    internal IslandService(Dispatcher dispatcher, IslandStore store, IslandConfig config)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Config = config;

        // Pano okuması STA worker'da: OpenClipboard bloklayabilir, UI thread'i kilitlememek
        // zorunlu. TrayMirrorService ile aynı kalıp.
        var staThread = new Thread(() =>
        {
            _staDispatcher = Dispatcher.CurrentDispatcher;
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WharfBar.Island",
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();

        _saveTimer = new System.Threading.Timer(
            _ => FlushSave(), null, Timeout.Infinite, Timeout.Infinite);

        if (Config.Enabled)
        {
            StartListening();
        }
    }

    internal IslandConfig Config { get; private set; }

    internal IReadOnlyList<IslandEntry> Entries => _entries;

    internal IslandEntry? Latest => _entries.Count > 0 ? _entries[0] : null;

    internal event EventHandler? Changed;

    /// <summary>Yeni kayıt eklendiğinde pill'in nabzını tetiklemek için dışarıdan okunur.</summary>
    internal event EventHandler? EntryAdded;

    internal void ApplyConfig(IslandConfig config)
    {
        Config = config;
        if (Config.Enabled)
        {
            StartListening();
        }
        else
        {
            StopListening();
        }

        RaiseChanged();
    }

    private void StartListening()
    {
        if (_listener is not null || _disposed)
        {
            return;
        }

        _listener = new ClipboardMessageWindow();
        _listener.ClipboardChanged += OnClipboardChanged;
    }

    private void StopListening()
    {
        if (_listener is null)
        {
            return;
        }

        _listener.ClipboardChanged -= OnClipboardChanged;
        _listener.Dispose();
        _listener = null;
    }

    /// <summary>UI thread'inde tetiklenir; okuma işini STA worker'a devreder.</summary>
    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        Dispatcher? sta = _staDispatcher;
        if (sta is null || _disposed)
        {
            return;
        }

        _ = sta.BeginInvoke(
            () =>
            {
                try
                {
                    CaptureFromClipboard();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WharfBar] island capture failed: {ex.Message}");
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>STA worker'da çalışır.</summary>
    private void CaptureFromClipboard()
    {
        if (!TryReadWithRetry(out string? text, out bool optOut, out int byteCount))
        {
            return;
        }

        if (!IslandPruner.ShouldCapture(optOut, text, byteCount))
        {
            return;
        }

        AddEntry(new IslandEntry(
            Interlocked.Increment(ref _nextId),
            IslandEntryKind.Text,
            text!,
            null,
            DateTime.UtcNow,
            ClipboardNative.GetOwnerProcessName()));
    }

    private static bool TryReadWithRetry(out string? text, out bool optOut, out int byteCount)
    {
        for (int attempt = 0; attempt < OpenClipboardRetries; attempt++)
        {
            if (ClipboardNative.TryReadText(out text, out optOut, out byteCount))
            {
                return true;
            }

            if (optOut)
            {
                return false;
            }

            if (attempt < OpenClipboardRetries - 1)
            {
                Thread.Sleep(OpenClipboardRetryDelayMs);
            }
        }

        text = null;
        optOut = false;
        byteCount = 0;
        return false;
    }

    private void AddEntry(IslandEntry entry)
    {
        IReadOnlyList<IslandEntry> merged = IslandPruner.Prepend(
            _entries,
            entry,
            Config.MaxItems,
            DateTimeOffset.UtcNow,
            Config.RetentionDays,
            Config.Persistence == IslandConfig.PersistenceMode.Persistent);

        ApplySnapshot(merged, entry, raiseEntryAdded: true);
    }

    /// <summary>UI thread'inde çağrılır.</summary>
    internal void AddDroppedFiles(IReadOnlyList<string> sourcePaths)
    {
        if (_disposed || sourcePaths.Count == 0)
        {
            return;
        }

        List<IslandEntry> added = [];
        foreach (string source in sourcePaths)
        {
            if (!File.Exists(source))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(_store.DropRoot);
                string target = UniqueDropPath(Path.GetFileName(source));
                File.Copy(source, target, overwrite: false);
                added.Add(new IslandEntry(
                    Interlocked.Increment(ref _nextId),
                    IslandEntryKind.File,
                    Path.GetFileName(target),
                    target,
                    DateTime.UtcNow,
                    null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[WharfBar] island drop copy failed for {source}: {ex.Message}");
            }
        }

        if (added.Count == 0)
        {
            return;
        }

        IReadOnlyList<IslandEntry> merged = _entries;
        IslandEntry? last = null;
        foreach (IslandEntry e in added)
        {
            merged = IslandPruner.Prepend(
                merged, e, Config.MaxItems, DateTimeOffset.UtcNow,
                Config.RetentionDays, Config.Persistence == IslandConfig.PersistenceMode.Persistent);
            last = e;
        }

        ApplySnapshot(merged, last, raiseEntryAdded: true);
    }

    private string UniqueDropPath(string fileName)
    {
        string candidate = Path.Combine(_store.DropRoot, fileName);
        int suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(_store.DropRoot, IslandPruner.BuildDropFileName(fileName, suffix));
            suffix++;
        }

        return candidate;
    }

    /// <summary>UI thread'inde çağrılır. Metni panoya yazar (yapıştırma yapılmaz).</summary>
    internal void PutBack(IslandEntry entry)
    {
        if (entry.Kind == IslandEntryKind.File)
        {
            return;
        }

        Dispatcher? sta = _staDispatcher;
        if (sta is null)
        {
            return;
        }

        _ = sta.BeginInvoke(
            () =>
            {
                try
                {
                    if (!ClipboardNative.TryWriteText(entry.Text))
                    {
                        Debug.WriteLine("[WharfBar] island put-back write failed");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WharfBar] island put-back failed: {ex.Message}");
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>UI thread'inde çağrılır.</summary>
    internal void Remove(IslandEntry entry)
    {
        IReadOnlyList<IslandEntry> next = _entries.Where(e => e.Id != entry.Id).ToList();
        ApplySnapshot(next, entry, raiseEntryAdded: false);
    }

    /// <summary>UI thread'inde çağrılır.</summary>
    internal void Clear() => ApplySnapshot([], null, raiseEntryAdded: false);

    /// <summary>UI thread'inde çağrılır. Yalnız bırakılan dosyaları siler, panoya dokunmaz.</summary>
    internal void DeleteDroppedFiles()
    {
        foreach (IslandEntry e in _entries.Where(x => x.Kind == IslandEntryKind.File))
        {
            if (e.FilePath is null || !File.Exists(e.FilePath))
            {
                continue;
            }

            try
            {
                File.Delete(e.FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[WharfBar] island drop delete failed: {ex.Message}");
            }
        }
    }

    private void ApplySnapshot(IReadOnlyList<IslandEntry> next, IslandEntry? added, bool raiseEntryAdded)
    {
        if (_dispatcher.CheckAccess())
        {
            Commit(next, added, raiseEntryAdded);
        }
        else
        {
            _dispatcher.BeginInvoke(() => Commit(next, added, raiseEntryAdded), DispatcherPriority.Background);
        }
    }

    private void Commit(IReadOnlyList<IslandEntry> next, IslandEntry? added, bool raiseEntryAdded)
    {
        _entries.Clear();
        _entries.AddRange(next);

        if (Config.Persistence == IslandConfig.PersistenceMode.Persistent)
        {
            ScheduleSave();
        }

        RaiseChanged();

        if (raiseEntryAdded && added is not null)
        {
            EntryAdded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty), DispatcherPriority.Background);
        }
    }

    private void ScheduleSave()
    {
        _savePending = true;
        _saveTimer?.Change(SaveDebounceMs, Timeout.Infinite);
    }

    private void FlushSave()
    {
        if (!_savePending)
        {
            return;
        }

        _savePending = false;
        List<IslandEntry> snapshot = [.. _entries];
        ThreadPool.QueueUserWorkItem(_ => _store.Save(snapshot));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopListening();
        _saveTimer?.Dispose();

        // Pending debounced write must not be lost on shutdown.
        FlushSave();

        _staDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        _staDispatcher = null;
    }
}
```

- [ ] **Step 2: `TopBarEngine.cs`'e alan ve Dispose ekle**

`src/WharfBar/Services/TopBarEngine.cs` içinde `internal VeilService? Veil { get; private set; }` benzeri bir property bulunduğu satırın altına ekle:

```csharp
    /// <summary>Clipboard geçmişi + bırakılan dosyalar servisi (null = ada kapalı).</summary>
    internal IslandService? Island { get; private set; }
```

Engine'in servisleri kurduğu ctor bloğuna ekle:

```csharp
        if (Config.Current.Island.Enabled)
        {
            Island = new IslandService(
                dispatcher,
                new IslandStore(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WharfBar")),
                Config.Current.Island);
        }
```

`Dispose()` metodunda mevcut servis sıralamasına göre **ilk sıraya** ekle (diğerleri bağımlı değil, ama bar önce kapanmalı):

```csharp
        Island?.Dispose();
        Island = null;
```

`using System.IO;` eksikse dosyanın başına ekle.

- [ ] **Step 3: Derle**

Run: `dotnet build WharfBar.sln -v quiet --nologo`
Expected: `0 Hata`, `0 Uyarı`.

- [ ] **Step 4: Testleri çalıştır, yeşil olduğunu doğrula**

Run: `dotnet test WharfBar.sln --nologo -v quiet`
Expected: `Başarılı! - Başarısız: 0, Başarılı: 350`.

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Services/IslandService.cs src/WharfBar/Services/TopBarEngine.cs
git commit -m "feat(island): servis orkestrasyonu, STA okuma thread'i, debounce yazma"
```

---

## Task 6: Pill ve panel

**Files:**
- Create: `src/WharfBar/Views/IslandFlyout.xaml`, `src/WharfBar/Views/IslandFlyout.xaml.cs`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml` (satır 133 `StatusStrip`'ten önce)
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `IslandService` (Task 5) — `TopBarWindow` engine'den `Engine.Island` üzerinden alır
- Produces: `namespace WharfBar.Views` → `internal sealed partial class IslandFlyout : UserControl`:
  - `ctor()`
  - `void Attach(IslandService service)`
  - `void Detach()`
  - `void OnPanelOpened()` / `void OnPanelClosed()`

- [ ] **Step 1: `IslandFlyout.xaml` oluştur**

`src/WharfBar/Views/IslandFlyout.xaml`:

```xml
<UserControl x:Class="WharfBar.Views.IslandFlyout"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             Width="420"
             MaxHeight="440">
    <Border x:Name="FlyoutRoot"
            Background="{DynamicResource ApplicationBackgroundBrush}"
            CornerRadius="12"
            Padding="12"
            Margin="0,0,12,0"
            SnapsToDevicePixels="True">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>

            <TextBox x:Name="SearchBox"
                     Grid.Row="0"
                     Margin="0,0,0,8"
                     Padding="8,5"
                     TextChanged="OnSearchChanged"
                     ToolTipService.InitialShowDelay="800" />

            <!-- Popup içinde iç içe Popup YASAK (docs/04-gotchas.md): liste ScrollViewer olmalı. -->
            <ScrollViewer x:Name="ListScroll"
                          Grid.Row="1"
                          VerticalScrollBarVisibility="Auto"
                          HorizontalScrollBarVisibility="Disabled"
                          Focusable="False">
                <ItemsControl x:Name="EntryList" />
            </ScrollViewer>
        </Grid>
    </Border>
</UserControl>
```

- [ ] **Step 2: `IslandFlyout.xaml.cs` oluştur**

`src/WharfBar/Views/IslandFlyout.xaml.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Island paneli: arama kutusu + tek birleşik zaman çizelgesi. Düzen, satırları elle
/// kurar (projede MVVM yok); liste öğeleri <see cref="Border"/> satırlarıdır.
/// </summary>
public partial class IslandFlyout : UserControl
{
    private IslandService? _service;
    private readonly List<IslandEntry> _rows = [];
    private int _selectedIndex = -1;

    public IslandFlyout()
    {
        InitializeComponent();
        Loaded += (_, _) => FlyoutRoot.Background = FlyoutSurface.Create();
    }

    /// <summary>Servisi bağlar; önceki bağlantıyı koparır (idempotent).</summary>
    internal void Attach(IslandService service)
    {
        Detach();
        _service = service;
        service.Changed += OnChanged;
        Rebuild();
    }

    internal void Detach()
    {
        if (_service is not null)
        {
            _service.Changed -= OnChanged;
            _service = null;
        }
    }

    internal void OnPanelOpened()
    {
        SearchBox.Clear();
        _selectedIndex = _rows.Count > 0 ? 0 : -1;
        Rebuild();
        SearchBox.Focus();
    }

    internal void OnPanelClosed() => Detach();

    private void OnChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Rebuild);
            return;
        }

        Rebuild();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (_service is null)
        {
            return;
        }

        string? query = SearchBox.Text;
        var visible = _service.Entries.Where(e => IslandPruner.Matches(e, query)).ToList();

        if (_selectedIndex >= visible.Count)
        {
            _selectedIndex = visible.Count - 1;
        }

        _rows.Clear();
        _rows.AddRange(visible);

        var panel = new StackPanel();
        for (int i = 0; i < _rows.Count; i++)
        {
            panel.Children.Add(BuildRow(_rows[i], i == _selectedIndex));
        }

        if (_rows.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Kayıt yok.",
                Opacity = 0.6,
                Margin = new Thickness(4, 12, 4, 12),
            });
        }

        EntryList.Items.Clear();
        EntryList.Items.Add(panel);
    }

    private Border BuildRow(IslandEntry entry, bool selected)
    {
        var row = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(8),
            Background = selected
                ? (Application.Current.TryFindResource("ControlFillColorDefaultBrush") as Brush)
                ?? new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF))
                : Brushes.Transparent,
        };
        row.Tag = entry;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = entry.Kind == IslandEntryKind.File ? "📄" : "≡",
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

        var time = new TextBlock
        {
            Text = string.IsNullOrEmpty(entry.SourceApp)
                ? IslandPruner.FormatRelativeTime(entry.CapturedAtUtc, DateTimeOffset.UtcNow)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} · {1}",
                    entry.SourceApp.Length > IslandPruner.SourceAppMaxChars
                        ? entry.SourceApp[..IslandPruner.SourceAppMaxChars]
                        : entry.SourceApp,
                    IslandPruner.FormatRelativeTime(entry.CapturedAtUtc, DateTimeOffset.UtcNow)),
            Opacity = 0.6,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(time, 2);
        grid.Children.Add(time);

        row.Child = grid;

        row.MouseLeftButtonUp += (_, _) => Activate(entry);
        row.KeyDown += OnRowKeyDown;
        return row;
    }

    private void OnRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _selectedIndex >= 0 && _selectedIndex < _rows.Count)
        {
            Activate(_rows[_selectedIndex]);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedIndex >= 0 && _selectedIndex < _rows.Count)
        {
            _service?.Remove(_rows[_selectedIndex]);
            e.Handled = true;
        }
    }

    private void Activate(IslandEntry entry)
    {
        if (_service is null)
        {
            return;
        }

        if (entry.Kind == IslandEntryKind.File && entry.FilePath is not null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = entry.FilePath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WharfBar] island open failed: {ex.Message}");
            }

            return;
        }

        _service.PutBack(entry);
    }

    /// <summary>Liste klavye gezinmesi; panel açıldığında arama kutusuna bağlanır.</summary>
    internal void HandleNavigationKey(Key key)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        switch (key)
        {
            case Key.Down:
                _selectedIndex = Math.Min(_rows.Count - 1, _selectedIndex + 1);
                break;
            case Key.Up:
                _selectedIndex = Math.Max(0, _selectedIndex - 1);
                break;
            case Key.Enter:
            case Key.Delete:
                OnRowKeyDown(this, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this) ?? throw new InvalidOperationException(), 0, key)
                {
                    RoutedEvent = Keyboard.KeyDownEvent,
                });
                break;
            default:
                return;
        }

        Rebuild();
    }
}
```

- [ ] **Step 3: `TopBarWindow.xaml`'e pill ve popup ekle**

`src/WharfBar/Views/TopBarWindow.xaml` içinde, `<StackPanel x:Name="StatusStrip"` satırından **önce** ekle:

```xml
            <!-- Island: Dynamic-Island benzeri pano + bırakılan dosya alanı (birincil monitör) -->
            <Border x:Name="IslandPill"
                    HorizontalAlignment="Center"
                    VerticalAlignment="Center"
                    Background="Transparent"
                    Padding="10,2"
                    CornerRadius="12"
                    Cursor="Hand"
                    Visibility="Collapsed"
                    AllowDrop="True"
                    ToolTipService.InitialShowDelay="800">
                <StackPanel Orientation="Horizontal">
                    <TextBlock x:Name="IslandGlyph"
                               Text="≡"
                               Margin="0,0,6,0"
                               VerticalAlignment="Center" />
                    <TextBlock x:Name="IslandPreviewText"
                               TextTrimming="CharacterEllipsis"
                               MaxWidth="240"
                               VerticalAlignment="Center" />
                    <TextBlock x:Name="IslandCountText"
                               Margin="8,0,0,0"
                               Opacity="0.6"
                               VerticalAlignment="Center" />
                </StackPanel>
            </Border>
```

Dosyanın sonlarındaki `</Grid>` kapanışından önce ekle:

```xml
            <Popup x:Name="IslandPopup"
                   Placement="Bottom"
                   PlacementTarget="{Binding ElementName=IslandPill}"
                   StaysOpen="True"
                   AllowsTransparency="True"
                   PopupAnimation="None"
                   VerticalOffset="6"
                   Opened="OnIslandPopupOpened"
                   Closed="OnIslandPopupClosed">
                <local:IslandFlyout x:Name="IslandHost" />
            </Popup>
```

- [ ] **Step 4: `TopBarWindow.xaml.cs`'e durum makinesi ve bağlantıyı ekle**

`ToggleQuickSettings` metodunun hemen altına ekle:

```csharp
    private bool _islandPulseRunning;

    private void ToggleIsland()
    {
        if (IslandService is null)
        {
            return;
        }

        CancelPendingClose(IslandHost, ref _islandClosePending);
        if (IslandPopup.IsOpen)
        {
            CloseIsland();
            return;
        }

        IslandPopup.IsOpen = true;
    }

    private void CloseIsland(bool animate = true)
    {
        if (!IslandPopup.IsOpen)
        {
            _islandClosePending = false;
            return;
        }

        if (animate && !_islandClosePending)
        {
            _islandClosePending = true;
            AnimateFlyoutClose(IslandHost, () =>
            {
                _islandClosePending = false;
                IslandPopup.IsOpen = false;
            });
        }
        else
        {
            CancelPendingClose(IslandHost, ref _islandClosePending);
            IslandPopup.IsOpen = false;
        }
    }

    private void OnIslandPopupOpened(object sender, EventArgs e)
    {
        IslandHost.OnPanelOpened();
        AnimateFlyoutOpen(IslandHost);
    }

    private void OnIslandPopupClosed(object sender, EventArgs e)
    {
        IslandHost.OnPanelClosed();
    }

    private void OnIslandPillClick(object sender, MouseButtonEventArgs e) => ToggleIsland();

    private void UpdateIslandPill()
    {
        IslandService? island = IslandService;
        if (island is null)
        {
            IslandPill.Visibility = Visibility.Collapsed;
            return;
        }

        IslandPill.Visibility = Visibility.Visible;
        IslandEntry? latest = island.Latest;
        IslandPreviewText.Text = latest is null
            ? "Boş"
            : IslandPruner.FormatPreview(latest.Text);
        IslandGlyph.Text = latest is null ? "≡" : latest.Kind == IslandEntryKind.File ? "📄" : "≡";
        IslandCountText.Text = island.Entries.Count > 0
            ? island.Entries.Count.ToString(CultureInfo.CurrentCulture)
            : string.Empty;
    }

    private void PulseIslandPill()
    {
        if (_islandPulseRunning)
        {
            return;
        }

        _islandPulseRunning = true;
        var storyboard = new Storyboard();
        var scale = new DoubleAnimation
        {
            From = 1.0,
            To = 1.06,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            AutoReverse = true,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTargetProperty(scale, new PropertyPath(RenderTransformProperty));
        var transform = new ScaleTransform();
        IslandPill.RenderTransform = transform;
        Storyboard.SetTarget(scale, IslandPill);
        storyboard.Children.Add(scale);
        storyboard.Completed += (_, _) => _islandPulseRunning = false;
        storyboard.Begin();
    }
```

`CloseFlyouts` metoduna `CloseIsland(animate);` ekle:

```csharp
    private void CloseFlyouts(bool animate = true)
    {
        CloseIsland(animate);
        CloseQuickSettings(animate);
        CloseBrightness(animate);
        CloseMedia(animate);
    }
```

- [ ] **Step 5: Servis bağlantısını kur, sürükle-bırak ve klavye handler'larını ekle**

`TopBarWindow.xaml.cs` içinde `IslandService` adlı özellik ekle (engine'den alır):

```csharp
    private IslandService? IslandService => _engine?.Island;
```

Engine kurulumundan sonra, `UpdateIslandPill()`'i çağıran bağlantıyı kur. `AttachIsland()` metodu:

```csharp
    private void AttachIsland()
    {
        IslandService? island = IslandService;
        if (island is null)
        {
            UpdateIslandPill();
            return;
        }

        island.Changed += OnIslandChanged;
        island.EntryAdded += OnIslandEntryAdded;
        UpdateIslandPill();
    }

    private void OnIslandChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateIslandPill);

    private void OnIslandEntryAdded(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            UpdateIslandPill();
            PulseIslandPill();
        });

    private void DetachIsland()
    {
        if (IslandService is { } island)
        {
            island.Changed -= OnIslandChanged;
            island.EntryAdded -= OnIslandEntryAdded;
        }
    }
```

`TopBarWindow`'ın motor kurulumunu tamamladığı yere `AttachIsland();` çağrısı ekle ve `DisposeOwnedServices` başına `DetachIsland();` ekle.

Sürükle-bırak handler'ları (aynı sınıfa ekle):

```csharp
    private void OnIslandPillDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || IslandService is not { } island)
        {
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            island.AddDroppedFiles(paths);
        }

        e.Handled = true;
        UpdateIslandPill();
    }

    private void OnIslandDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
```

`IslandPill` için XAML'de `MouseLeftButtonUp="OnIslandPillClick"`, `Drop="OnIslandPillDrop"` ve `DragOver="OnIslandDragOver"` niteliklerini `Border` açılışına ekle.

Panel klavye gezinmesi için `IslandHost` alanına `PreviewKeyDown="OnIslandKeyDown"` bağla ve handler'ı ekle:

```csharp
    private void OnIslandKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseIsland();
            e.Handled = true;
            return;
        }

        IslandHost.HandleNavigationKey(e.Key);
    }
```

Gerekli `using` ekleri: `System.Windows.Media.Animation`, `System.Globalization`, `WharfBar.Models`, `WharfBar.Services`.

- [ ] **Step 6: Derle ve canlı doğrula**

Run: `dotnet build WharfBar.sln -v quiet --nologo`
Expected: `0 Hata`.

Uygulamayı normal yoldan çalıştır (`dotnet run --project src/WharfBar`) ve şunları doğrula:
1. Üst barın ortasında ada görünüyor.
2. Bir metin kopyala → ada önizlemesi belirir ve kısa bir nabız oynar.
3. Adaya tıkla → panel açılır, kopyaladığın metin listede.
4. Panele yazınca liste filtrelenir.
5. `↑`/`↓` gezinir, `Enter` panoya koyar, `Delete` siler, `Esc` kapatır.
6. Panel dışına tıklayınca kapanır.
7. Bir dosyayı adaya sürükle → `Drop` klasörüne kopyalanır ve listede belirir.

- [ ] **Step 7: Commit**

```bash
git add src/WharfBar/Views/IslandFlyout.xaml src/WharfBar/Views/IslandFlyout.xaml.cs src/WharfBar/Views/TopBarWindow.xaml src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "feat(island): pill ve panel - arama, tek tikla geri koyma, surukle birakma"
```

---

## Task 7: Ayar sayfası

**Files:**
- Create: `src/WharfBar/Views/Pages/IslandPage.xaml`, `src/WharfBar/Views/Pages/IslandPage.xaml.cs`
- Modify: `src/WharfBar/Views/SettingsWindow.xaml`

**Interfaces:**
- Consumes: `IslandConfig`/`IslandNormalizer` (Task 2), `IslandService` (Task 5)
- Produces: `WharfBar.Views.Pages.IslandPage` (XAML page, `TargetPageType` ile navigasyon)

- [ ] **Step 1: Nav ikon enum adını doğrula (ZORUNLU)**

XAML'e ikon yazmadan önce enum adının gerçekten var olduğunu kanıtla. Yansıma yerine
**build çıktısındaki `Wpf.Ui.dll`** içinde UTF-8 olarak aranır (enum isimleri metadata
#Strings heap'inde UTF-8 saklanır; UTF-16 taraması hiçbir şey bulmaz):

```bash
$dll = "src\WharfBar\bin\Debug\net8.0-windows10.0.19041.0\Wpf.Ui.dll"
$text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($dll))
[regex]::Matches($text, '\b(?:Clip|Paste|Copy|Attach)[A-Za-z0-9]{0,20}\b') | ForEach-Object { $_.Value } | Sort-Object -Unique
```

Bu komut 2026-10-01'de çalıştırıldı ve **`Clipboard20`** doğrulandı (ayrıca
`Clipboard16/24/28/32/48`, `ClipboardArrowRight16`, `AttachText20/24` mevcut). Bu yüzden
aşağıdaki XAML'de `Clipboard20` kullanılmıştır — **adı değiştiriyorsan aynı komutla yeniden
doğrula.**

- [ ] **Step 2: `IslandPage.xaml` oluştur**

`src/WharfBar/Views/Pages/IslandPage.xaml` (ikon adı Step 1'de doğrulandı: `Clipboard20`):

```xml
<Page x:Class="WharfBar.Views.Pages.IslandPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ui="http://schemas.microsoft.com/winfx/wpfui"
      xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
      xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
      mc:Ignorable="d"
      Title="Ada">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="16">
            <Border Style="{StaticResource PowerToysCard}" Padding="16" Margin="0,0,0,12">
                <StackPanel>
                    <TextBlock Text="Ada" FontWeight="SemiBold" FontSize="16" Margin="0,0,0,4" />
                    <TextBlock Text="Pano geçmişi ve island'a bırakılan dosyalar."
                               Opacity="0.7"
                               TextWrapping="Wrap"
                               Margin="0,0,0,12" />
                    <ui:ToggleSwitch x:Name="EnabledSwitch"
                                     Header="Adayı göster"
                                     OnContent="Açık"
                                     OffContent="Kapalı"
                                     Toggled="OnEnabledToggled" />
                </StackPanel>
            </Border>

            <Border Style="{StaticResource PowerToysCard}" Padding="16" Margin="0,0,0,12">
                <StackPanel>
                    <TextBlock Text="Geçmiş" FontWeight="SemiBold" FontSize="16" Margin="0,0,0,8" />
                    <ui:RadioButtons x:Name="PersistenceGroup"
                                     Header="Saklama"
                                     SelectionChanged="OnPersistenceChanged">
                        <ui:RadioButtons.Items>
                            <ui:RadioButton Content="Oturumluk (kapanınca silinir)" />
                            <ui:RadioButton Content="Kalıcı (diske yazılır)" />
                        </ui:RadioButtons.Items>
                    </ui:RadioButtons>
                    <ui:NumberBox x:Name="RetentionBox"
                                  Header="Saklama süresi (gün)"
                                  Minimum="1"
                                  Maximum="3650"
                                      Margin="0,8,0,0"
                                  ValueChanged="OnRetentionChanged" />
                    <ui:NumberBox x:Name="MaxItemsBox"
                                  Header="En fazla kayıt"
                                  Minimum="10"
                                  Maximum="5000"
                                  Margin="0,8,0,0"
                                  ValueChanged="OnMaxItemsChanged" />
                </StackPanel>
            </Border>

            <Border Style="{StaticResource PowerToysCard}" Padding="16">
                <StackPanel>
                    <TextBlock Text="Bakım" FontWeight="SemiBold" FontSize="16" Margin="0,0,0,8" />
                    <ui:Button Content="Drop klasörünü aç" Click="OnOpenDropFolder" Margin="0,0,0,8" />
                    <ui:Button Content="Bırakılan dosyaları sil" Click="OnDeleteDroppedFiles" Margin="0,0,0,8" />
                    <ui:Button Content="Tüm geçmişi temizle" Click="OnClearHistory" />
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 3: `IslandPage.xaml.cs` oluştur**

`src/WharfBar/Views/Pages/IslandPage.xaml.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

public partial class IslandPage : Page
{
    private bool _isLoading = true;

    public IslandPage()
    {
        InitializeComponent();
        LoadSettings();
        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;

    private ConfigService Config => WharfApp.Config!;

    private AppConfig Cfg => Config.Current;

    private IslandService? Island => WharfApp.Monitors?.Engine?.Island;

    private void LoadSettings()
    {
        IslandConfig island = Cfg.Island;
        EnabledSwitch.IsChecked = island.Enabled;
        PersistenceGroup.SelectedIndex = (int)island.Persistence;
        RetentionBox.Value = island.RetentionDays;
        MaxItemsBox.Value = island.MaxItems;
    }

    private void SaveAndApply()
    {
        if (_isLoading)
        {
            return;
        }

        Config.Save(Cfg);
        Island?.ApplyConfig(Cfg.Island);
    }

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        Cfg.Island.Enabled = EnabledSwitch.IsChecked == true;
        SaveAndApply();
    }

    private void OnPersistenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || PersistenceGroup.SelectedIndex < 0)
        {
            return;
        }

        Cfg.Island.Persistence = (IslandConfig.PersistenceMode)PersistenceGroup.SelectedIndex;
        SaveAndApply();
    }

    private void OnRetentionChanged(object sender, ControlChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Island.RetentionDays = IslandNormalizer.ClampRetentionDays((int)e.NewValue);
        SaveAndApply();
    }

    private void OnMaxItemsChanged(object sender, ControlChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Island.MaxItems = IslandNormalizer.ClampMaxItems((int)e.NewValue);
        SaveAndApply();
    }

    private void OnOpenDropFolder(object sender, RoutedEventArgs e)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WharfBar", "Drop");
        System.IO.Directory.CreateDirectory(root);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WharfBar] open drop folder failed: {ex.Message}");
        }
    }

    private void OnDeleteDroppedFiles(object sender, RoutedEventArgs e)
    {
        if (Island is null)
        {
            return;
        }

        Island.DeleteDroppedFiles();
        Island.Clear();
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        Island?.Clear();
    }
}
```

Gerekli `using` ekleri: `System.IO`, `System.Windows.Controls.Primitives` (`ControlChangedEventArgs<T>` için gerekirse).

- [ ] **Step 4: `SettingsWindow.xaml`'e nav girdisi ekle**

`src/WharfBar/Views/SettingsWindow.xaml` içinde, `Content="Kaplama"` olan `NavigationViewItem`'tan **sonra** ekle (ikon adı Step 1'de doğrulanan ad olmalı):

```xml
    <ui:NavigationViewItem Content="Ada"
                            TargetPageType="{x:Type pages:IslandPage}">
        <ui:NavigationViewItem.Icon>
            <ui:SymbolIcon Symbol="Clipboard20" />
        </ui:NavigationViewItem.Icon>
    </ui:NavigationViewItem>
```

- [ ] **Step 5: Derle ve doğrula**

Run: `dotnet build WharfBar.sln -v quiet --nologo`
Expected: `0 Hata`. **Yanlış ikon enum adı prosesi burada öldürür** — hata alırsan Step 1'e dön.

Uygulamayı çalıştırıp `Ayarlar → Ada` sayfasını aç: açık/kapalı, kalıcılık seçimi, gün/kayıt sayıları çalışıyor olmalı; "Tüm geçmişi temizle" listeyi boşaltmalı.

- [ ] **Step 6: Commit**

```bash
git add src/WharfBar/Views/Pages/IslandPage.xaml src/WharfBar/Views/Pages/IslandPage.xaml.cs src/WharfBar/Views/SettingsWindow.xaml
git commit -m "feat(island): ayar sayfasi - kalicilik, limitler, bakim"
```

---

## Task 8: Dokümantasyon ve uçtan uca doğrulama

**Files:**
- Create: `docs/decisions/010-island-clipboard-drop.md`
- Modify: `docs/01-architecture.md`, `docs/04-gotchas.md`, `.agent/boundaries.md`, `.agent/context/roadmap.md`

**Interfaces:**
- Consumes: (yok — bu görev yalnız dokümante eder ve doğrular)
- Produces: (yeni ADR + güncel dokümanlar)

- [ ] **Step 1: ADR-010 yaz**

`docs/decisions/010-island-clipboard-drop.md` olarak oluştur. İçerik: karar (üst barın içinde Dynamic Island, bağımsız pencere reddedildi), gerekçe (Coucou/NotchBuddy konsepti; Windows'ta notch yok, WharfBar'ın bar'ı bu rolü oynar), seçilen yolun reddedilen alternatifleri ve nedenleri (B: `Topmost` pencere — z-order/focus/Mica riski; C: bar'a sahip ayrı pencere — A'nın avantajları yok), kapsam dışı bölümü (karakter/ses lisansı — Coucou "Mochi" ve sesleri © Louis Raillé, kod MIT; `CF_HDROP` yakalama; klasör bırakma; global hotkey), ölçülen tuzak (`ABM_SETSTATE` yalnız kayıtlı AppBar'dan etkili — 2026-09-30'da yanlış sonuca düşüldü).

- [ ] **Step 2: Mimari ve tuzak dokümanlarını güncelle**

`docs/01-architecture.md` içine "Island" alt bölümü ekle: yeni dosya listesi ve görevleri, `Popup` paneli, STA okuma thread'i, ayrı kalıcılık dosyası.

`docs/04-gotchas.md` tablosuna şu satırları ekle:
- **Pano okuması UI thread'inde** → `OpenClipboard` bloklayabilir; STA worker'da okunur, kilitliyse birkaç kez kısa bekleyip sessizce düşülür.
- **Pano geçmişi `config.json`'a yazılmaz** → `ConfigService.Save` UI thread'inde tüm dosyayı yeniden yazar; ayrı atomik store kullanılır.
- **`Popup` içinde iç içe `Popup`** → mevcut kayıt; island listesi `ScrollViewer` + `ItemsControl`.
- **Pill ikonu** → `TopBarWindow` Grid'inin orta sütunu boştu; `StatusStrip` sağa hizalı, sol küme sola. Island ortaya yerleşir.

`.agent/boundaries.md` "Yap" listesine ekle: **Island/clipboard geçmişi** (belgelenmiş pano P/Invoke'ları, mesaj-only dinleyici, ayrı atomik store, mevcut flyout kalıbı; `CanIncludeInClipboardHistory` opt-out'una saygı; yeni paket yok).

`.agent/context/roadmap.md` içine tamamlanmış öğe olarak ekle.

- [ ] **Step 3: Tüm testleri ve derlemeyi doğrula**

Run: `dotnet build WharfBar.sln -v quiet --nologo && dotnet test WharfBar.sln --nologo -v quiet`
Expected: `0 Hata`, `Başarısılı! - Başarısız: 0`.

- [ ] **Step 4: Canlı uçtan uca doğrulama**

Uygulamayı normal yollardan çalıştır ve **normal yoldan kapat** (`boundaries.md`: zorla sonlandırma `ABM_REMOVE`'yi atlar, görev çubuğu ancak watchdog ile geri gelir, rezerve şerit `explorer.exe` yeniden başlatmayı gerektirir).

Doğrulanacaklar:
1. Ada üst barın ortasında görünür.
2. Metin kopyalanınca önizleme + nabız.
3. Panel açılır, arama filtreler, `Enter` panoya koyar, `Delete` siler, `Esc` kapatır.
4. Dosya bırakma `Drop` klasörüne kopyalar ve listede belirir.
5. **Persistent moda geç, kapat, yeniden aç** → geçmiş geri gelir. `island.json` diske yazılmış olmalı.
6. **RetentionDays=1 yap**, 2+ günlük sahte zaman damgalı bir kayıtla budamanın çalıştığını doğrula.
7. `Enabled=false` yap → ada kaybolur, pano dinleyicisi kapanır.
8. Çıkışta görev çubuğu ve work area baseline'a dönüyor (`ABM_GETSTATE=0`, `SPI_GETWORKAREA.Bottom=1032`).

- [ ] **Step 5: Geçici tanılama dosyasını temizle**

`TrayCapture` için yazdığın PNG dökümü varsa kaldır. `TrayPeekDiagnostics` hâlâ `TEMPORARY` olarak işaretliyse ve bu iş gerektirmiyorsa dosyayı ve `Log`/`LogNote` çağrılarını kaldır — bu plan sırasında o tanılama gerekmiyor.

- [ ] **Step 6: Commit**

```bash
git add docs/ src/WharfBar/Native/TrayCapture.cs src/WharfBar/Native/TrayPeekDiagnostics.cs .agent/
git commit -m "docs(island): ADR-010, mimari ve tuzak dokumanlari"
```

---

## Self-Review

**1. Spec kapsamı — her madde bir göreve karşılık geliyor mu?**

| Spec maddesi | Görev |
|---|---|
| Form faktörü: üst barın ortasında, `Popup` paneli | Task 6 |
| Kapalı görünüm: pill, tür ikonu, ~40 karakter önizleme, kaynak uygulama, ~280px | Task 1 (`FormatPreview`), Task 6 (`UpdateIslandPill`, `IslandPreviewText MaxWidth=240`) |
| Yeni kayıt nabzı (~1.2 sn) | Task 6 (`PulseIslandPill`) |
| Veri modeli: tek birleşik zaman çizelgesi | Task 1 (`IslandEntryKind`), Task 5 (`_entries`) |
| Pano okuma STA thread'inde | Task 5 (STA dispatcher + `OnClipboardChanged`) |
| Ayrı dosya + atomik yazma, 750 ms debounce | Task 4 (`IslandStore`), Task 5 (`ScheduleSave`/`FlushSave`) |
| `CanIncludeInClipboardHistory` opt-out | Task 3 (`TryReadText` → `historyOptOut`), Task 1 (`ShouldCapture`) |
| Dedupe (ardışık aynı → yukarı) | Task 1 (`Prepend` + `IsSameContent`) |
| Kalıcılık ayarları (`Session`/`Persistent`, gün, tavan) | Task 2, Task 1 (`Prune`), Task 7 |
| "Panoya geri koy" (yapıştırma yok) | Task 3 (`TryWriteText`), Task 5 (`PutBack`), Task 6 (`Activate`) |
| Kaynak uygulama (`GetClipboardOwner`) | Task 3 (`GetOwnerProcessName`), Task 5 |
| Bırakma: kopyala, `Drop` klasörü, ad çakışması | Task 1 (`BuildDropFileName`), Task 5 (`AddDroppedFiles`, `UniqueDropPath`) |
| 64 KB üstü reddi, boş/boşluk reddi | Task 1 (`ShouldCapture`) |
| Monitör kapsamı: yalnız birincil | Task 6 (pillsiz `SecondaryTopBar`'da görünmez — `Engine.Island` tek örnek, `AttachIsland` yalnız primary'de çağrılır) |
| Panel: arama + `ScrollViewer` listesi, iç içe `Popup` yok | Task 6 |
| Klavye: `↑` `↓` `Enter` `Delete` `Esc` | Task 6 |
| Dört kapanma yolu | Task 6 (`CloseFlyouts` + `WM_ACTIVATEAPP` mevcut handler + `Esc`) |
| Animasyon: mevcut `AnimateFlyoutOpen/Close` | Task 6 |
| Ayar sayfası | Task 7 |
| Hata yönetimi tablosu (kilitli pano, bozuk dosya, disk hatası, çıkışta flush) | Task 3/4/5 |
| Kapsam dışı listesi | Task 8 (ADR) |
| Test: saf çekirdek + canlı gözlem | Task 1, 2, 4, 8 |

**Eksik kapsam yok.**

**2. Placeholder taraması** — kod bloklarında eksik bırakılmış hiçbir adım yok; tüm imzalar somut. Task 7 Step 1 bilinçli olarak bir doğrulama komutudur, placeholder değildir: ikon enum adı XAML'e **yazılmadan** kanıtlanmak zorundadır (yanlış ad prosesi öldürür).

**3. Tip tutarlılığı**

- `IslandPruner` imzaları: Task 1 tanımlar, Task 5 tüketir — `Prepend(current, entry, maxItems, now, retentionDays, persistent)`, `Prune(...)`, `Matches(entry, query)`, `FormatPreview(text)`, `BuildDropFileName(name, suffix)`, `FormatRelativeTime(capturedAtUtc, now)`, `ShouldCapture(historyOptOut, text, byteCount)`. ✔
- `IslandStore`: Task 4 tanımlar, Task 5 tüketir — `ctor(string appDataRoot)`, `StorePath`, `DropRoot`, `Load()`, `Save(entries)`. ✔
- `ClipboardNative`: Task 3 tanımlar, Task 5 tüketir — `TryReadText(out text, out historyOptOut, out byteCount)`, `TryWriteText`, `GetOwnerProcessName`, `WM_CLIPBOARDUPDATE`, `AddListener`/`RemoveListener`. ✔
- `IslandService`: Task 5 tanımlar, Task 6 ve 7 tüketir — `Entries`, `Latest`, `Changed`, `EntryAdded`, `ApplyConfig`, `AddDroppedFiles`, `PutBack`, `Remove`, `Clear`, `DeleteDroppedFiles`, `Dispose`. ✔
- `IslandConfig` alan adları: `Enabled`, `Persistence`, `RetentionDays`, `MaxItems` — Task 2 tanımlar, Task 7 kullanır. ✔
