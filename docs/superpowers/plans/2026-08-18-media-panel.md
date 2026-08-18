# Medya Paneli Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Üst barda çalan medyayı gösteren (yalnızca oturum aktifken görünür) bir öğe + tıklayınca açılan kapak/başlık/kontroller içeren flyout; Windows SMTC üzerinden Oynat/Duraklat + İleri/Geri + çoklu oturum seçici.

**Architecture:** `MediaSessionService` (WinRT `GlobalSystemMediaTransportControlsSessionManager` sarmalayıcı) + saf `MediaSessionSelector` ("en son aktif" politikası) + saf `MediaSnapshot` modeli + `MediaFlyout` view (BrightnessFlyout deseni) + TopBarWindow kablolaması (katalog öğesi `media`, dinamik görünürlük). WinRT olayları UI thread'e `Dispatcher` ile marshal edilir.

**Tech Stack:** .NET 8 WPF (`net8.0-windows10.0.19041.0` — WinRT projection zaten mevcut, yeni paket yok), `Windows.Media.Control`, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-18-media-panel-design.md`

## Global Constraints

- Yeni NuGet paketi yok; `AllowUnsafeBlocks` açılmaz (`.agent/boundaries.md` — "dış bağımlılık önce sor")
- Yeni config alanı yok; yalnızca katalog v4 migration (`CurrentStatusCatalogVersion` 3 → 4)
- Tüm WinRT olay/`await` devam noktaları UI thread'de olmalı (WPF `Dispatcher`)
- `BitmapImage` yalnızca UI thread'de üretilir; üretildikten sonra `Freeze()`
- Durum şeridi öğesi `media`: oturum yokken `Collapsed` (Battery/Volume deseni)
- Türkçe UI metinleri (repo dili); kod yorumları repo stilinde kısa Türkçe
- Commit mesajları repo stili: `feat(media): ...` / `test(media): ...` / `docs(media): ...`
- Build: `dotnet build WharfBar.sln`; test: `dotnet test`

---

### Task 1: `MediaSnapshot` modeli

**Files:**
- Create: `src/WharfBar/Models/MediaSnapshot.cs`
- Test: `tests/WharfBar.Tests/MediaSnapshotTests.cs`

**Interfaces:**
- Produces: `internal sealed record MediaSnapshot(string SessionId, string AppName, string Title, string Artist, bool IsPlaying, ImageSource? Thumbnail)` + `static MediaSnapshot Idle { get; }` (boş alanlar, null thumbnail) + `bool HasSession` (`SessionId.Length > 0`). Sonraki görevler (Task 4 servis, Task 5 flyout) bunu kullanır.
- Not: spec'teki `HasThumbnail` alanı `Thumbnail is not null` ile türetildiği için planda kaldırıldı (fazlalık).

- [ ] **Step 1: Failing test yaz**

```csharp
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class MediaSnapshotTests
{
    [Fact]
    public void Idle_HasNoSession()
    {
        Assert.False(MediaSnapshot.Idle.HasSession);
        Assert.Equal(string.Empty, MediaSnapshot.Idle.SessionId);
        Assert.Equal(string.Empty, MediaSnapshot.Idle.Title);
        Assert.Equal(string.Empty, MediaSnapshot.Idle.Artist);
        Assert.False(MediaSnapshot.Idle.IsPlaying);
        Assert.Null(MediaSnapshot.Idle.Thumbnail);
    }

    [Fact]
    public void Snapshot_WithSessionId_HasSession()
    {
        var snap = new MediaSnapshot("Spotify.exe", "Spotify", "Başlık", "Sanatçı", true, null);
        Assert.True(snap.HasSession);
        Assert.Equal("Spotify.exe", snap.SessionId);
        Assert.Equal("Spotify", snap.AppName);
        Assert.Equal("Başlık", snap.Title);
        Assert.Equal("Sanatçı", snap.Artist);
        Assert.True(snap.IsPlaying);
    }

    [Fact]
    public void Snapshot_Records_AreValueEqual()
    {
        var a = new MediaSnapshot("id", "app", "t", "a", false, null);
        var b = new MediaSnapshot("id", "app", "t", "a", false, null);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Snapshot_WithThumbnail_HasThumbnail()
    {
        var thumb = new BitmapImage();
        thumb.BeginInit();
        thumb.CacheOption = BitmapCacheOption.OnLoad;
        thumb.StreamSource = new MemoryStream([0xFF, 0xD8, 0xFF]);
        thumb.EndInit();
        var snap = new MediaSnapshot("id", "app", "t", "a", false, thumb);
        Assert.NotNull(snap.Thumbnail);
    }
}
```

- [ ] **Step 2: Testi çalıştır — fail beklenir**

Run: `dotnet test tests/WharfBar.Tests` (veya kök çözüm için `dotnet test`)
Expected: derleme hatası `MediaSnapshot` bulunamadı.

- [ ] **Step 3: Modeli yaz**

```csharp
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
```

- [ ] **Step 4: Testi çalıştır — pass beklenir**

Run: `dotnet test`
Expected: `MediaSnapshotTests` geçer (4 test).

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Models/MediaSnapshot.cs tests/WharfBar.Tests/MediaSnapshotTests.cs
git commit -m "feat(media): MediaSnapshot model + testler"
```

---

### Task 2: `MediaSessionSelector` (saf seçim politikası)

**Files:**
- Create: `src/WharfBar/Services/MediaSessionSelector.cs`
- Test: `tests/WharfBar.Tests/MediaSessionSelectorTests.cs`

**Interfaces:**
- Consumes: — (bağımsız)
- Produces: `internal sealed class MediaSessionSelector`:
  - `IReadOnlyList<string> Sessions` (en son aktif başta; yalnızca playing/paused oturumlar)
  - `string? Current` (`Sessions[0]` yoksa `null`)
  - `void AddSession(string id)` (bilinmeyen → sona ekle; mevcut → dokunma)
  - `void MarkActive(string id)` (bilinmeyen id → no-op; bilinen → başa taşı)
  - `void RemoveSession(string id)`
  - `string? MoveCurrent(int delta)` (current'ı delta kadar kaydır, sarmala; sonraki seçili id döner; boşsa `null`)
  - Task 4 (`MediaSessionService`) bunu kullanır.

- [ ] **Step 1: Failing test yaz**

```csharp
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MediaSessionSelectorTests
{
    [Fact]
    public void AddSession_Appends_And_Current_IsFirst()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        Assert.Equal(["A", "B"], sel.Sessions);
        Assert.Equal("A", sel.Current);
    }

    [Fact]
    public void MarkActive_MovesSessionToFront()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.MarkActive("B");
        Assert.Equal(["B", "A"], sel.Sessions);
        Assert.Equal("B", sel.Current);
    }

    [Fact]
    public void MarkActive_UnknownId_IsNoOp()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.MarkActive("X");
        Assert.Equal(["A"], sel.Sessions);
        Assert.Equal("A", sel.Current);
    }

    [Fact]
    public void AddSession_Existing_IsNoOp()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("A");
        Assert.Equal(["A"], sel.Sessions);
    }

    [Fact]
    public void RemoveSession_Current_FallsBackToMostRecentRemaining()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.MarkActive("A"); // A, B
        sel.RemoveSession("A");
        Assert.Equal("B", sel.Current);
        Assert.Equal(["B"], sel.Sessions);
    }

    [Fact]
    public void RemoveSession_All_CurrentIsNull()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.RemoveSession("A");
        Assert.Null(sel.Current);
        Assert.Empty(sel.Sessions);
    }

    [Fact]
    public void MoveCurrent_WrapsAround()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.AddSession("C");
        Assert.Equal("B", sel.MoveCurrent(1));   // [B, A, C]
        Assert.Equal("C", sel.MoveCurrent(1));   // [C, B, A]
        Assert.Equal("A", sel.MoveCurrent(1));   // [A, C, B]
        Assert.Equal("C", sel.MoveCurrent(-1));  // [C, A, B]
    }

    [Fact]
    public void MoveCurrent_Empty_ReturnsNull()
    {
        var sel = new MediaSessionSelector();
        Assert.Null(sel.MoveCurrent(1));
    }
}
```

- [ ] **Step 2: Testi çalıştır — fail beklenir**

Run: `dotnet test`
Expected: derleme hatası `MediaSessionSelector` bulunamadı.

- [ ] **Step 3: Seçiciyi yaz**

```csharp
using System.Collections.Generic;

namespace WharfBar.Services;

/// <summary>
/// Oturum seçim politikası: en son aktif oturum başta. Saf liste mantığı — unit testli.
/// Yalnızca playing/paused oturumlar listede tutulur (çağıran taraf yönetir).
/// </summary>
internal sealed class MediaSessionSelector
{
    private readonly List<string> _sessions = new();

    /// <summary>Oturum id'leri, en son aktif başta.</summary>
    internal IReadOnlyList<string> Sessions => _sessions;

    /// <summary>En son aktif oturum; yoksa null.</summary>
    internal string? Current => _sessions.Count > 0 ? _sessions[0] : null;

    /// <summary>Bilinmeyen id'yi sona ekler; mevcuda dokunmaz.</summary>
    internal void AddSession(string id)
    {
        if (!_sessions.Contains(id))
        {
            _sessions.Add(id);
        }
    }

    /// <summary>Bilinen id'yi başa taşır; bilinmeyen no-op.</summary>
    internal void MarkActive(string id)
    {
        if (_sessions.Remove(id))
        {
            _sessions.Insert(0, id);
        }
    }

    internal void RemoveSession(string id) => _sessions.Remove(id);

    /// <summary>Seçili oturumu delta kadar kaydırır (liste sonunda sarmalayıcı); yeni seçim döner.</summary>
    internal string? MoveCurrent(int delta)
    {
        if (_sessions.Count == 0)
        {
            return null;
        }

        int current = _sessions.IndexOf(Current!);
        int next = ((current + delta) % _sessions.Count + _sessions.Count) % _sessions.Count;
        string id = _sessions[next];
        _sessions.RemoveAt(next);
        _sessions.Insert(0, id);
        return id;
    }
}
```

- [ ] **Step 4: Testi çalıştır — pass beklenir**

Run: `dotnet test`
Expected: `MediaSessionSelectorTests` geçer (8 test).

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Services/MediaSessionSelector.cs tests/WharfBar.Tests/MediaSessionSelectorTests.cs
git commit -m "feat(media): oturum seçim politikası (son aktif) + testler"
```

---

### Task 3: `MediaSessionService` (SMTC sarmalayıcı)

**Files:**
- Create: `src/WharfBar/Services/MediaSessionService.cs`

**Interfaces:**
- Consumes: `MediaSnapshot` (Task 1), `MediaSessionSelector` (Task 2)
- Produces (Task 5 flyout ve Task 6 TopBarWindow kullanır):
  - `internal sealed class MediaSessionService : IDisposable`
  - `event EventHandler? Changed` (snapshot güncellenince)
  - `event EventHandler? SessionsChanged` (oturum listesi / seçim değişince)
  - `MediaSnapshot Snapshot { get; }`
  - `bool HasActiveSession { get; }` (`_selector.Current is not null`)
  - `IReadOnlyList<string> SessionIds { get; }`
  - `Task InitializeAsync()` — SMTC manager kur; hata → 10 sn sonra tek retry (DispatcherTimer), retry da hatalıysa pasif kalır
  - `void TogglePlayPause()` / `void Next()` / `void Previous()` (fire-and-forget, hata yutulur)
  - `void SelectNext()` / `void SelectPrevious()` (cheuron'lar; sarmalayıcı)
  - `void Dispose()` — olay aboneliklerini çöz, timer durdur

- [ ] **Step 1: Servisi yaz**

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Windows SMTC (GlobalSystemMediaTransportControlsSessionManager) üzerinden çalan medyayı
/// izler: snapshot (başlık/sanatçı/kapak/durum) + oynat/duraklat/ileri/geri + oturum seçici.
/// Tüm olaylar UI thread'e (ctor'un kurulduğu Dispatcher) marshal edilir.
/// </summary>
internal sealed class MediaSessionService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly MediaSessionSelector _selector = new();
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageSource> _thumbnails = new(StringComparer.Ordinal);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private MediaSnapshot _snapshot = MediaSnapshot.Idle;
    private DispatcherTimer? _retryTimer;

    internal MediaSessionService()
    {
        // UI thread'de kurulur (TopBarWindow.OnSourceInitialized).
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    internal event EventHandler? Changed;

    internal event EventHandler? SessionsChanged;

    internal MediaSnapshot Snapshot => _snapshot;

    internal bool HasActiveSession => _selector.Current is not null;

    internal IReadOnlyList<string> SessionIds => _selector.Sessions;

    internal async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            ReconcileSessions();
            RaiseSessions();
            if (_selector.Current is not null)
            {
                await ReloadCurrentAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] SMTC init başarısız: {ex.Message}");
            ScheduleRetry();
        }
    }

    internal void TogglePlayPause() => SendCommand(static s => s.TryTogglePlayPauseAsync());

    internal void Next() => SendCommand(static s => s.TrySkipNextAsync());

    internal void Previous() => SendCommand(static s => s.TrySkipPreviousAsync());

    internal void SelectNext() => SelectRelative(1);

    internal void SelectPrevious() => SelectRelative(-1);

    public void Dispose()
    {
        _retryTimer?.Stop();
        _retryTimer = null;

        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager = null;
        }

        foreach (string id in _sessions.Keys.ToList())
        {
            RemoveSessionInternal(id);
        }

        _thumbnails.Clear();
    }

    private void SelectRelative(int delta)
    {
        if (_selector.MoveCurrent(delta) is null)
        {
            return;
        }

        RaiseSessions();
        _ = ReloadCurrentAsync();
    }

    private void SendCommand(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        string? id = _selector.Current;
        if (id is null || !_sessions.TryGetValue(id, out var session))
        {
            return;
        }

        _ = InvokeCommandAsync(command, session);
    }

    private static async Task InvokeCommandAsync(
        Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command,
        GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            await command(session);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] komut başarısız (desteklenmeyen komut olabilir): {ex.Message}");
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            ReconcileSessions();
            RaiseSessions();
            if (_selector.Current is not null)
            {
                _ = ReloadCurrentAsync();
            }
            else
            {
                _snapshot = MediaSnapshot.Idle;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            string id = GetSessionId(sender);
            SyncSelectorForSession(id);
            RaiseSessions();
            if (id == _selector.Current)
            {
                _ = ReloadCurrentAsync();
            }
        });
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (GetSessionId(sender) == _selector.Current)
            {
                _ = ReloadCurrentAsync();
            }
        });
    }

    /// <summary>Manager'daki oturumları mevcut kümeyle eşitler: yenileri abone et + selector'a ekle, gidenleri çıkar.</summary>
    private void ReconcileSessions()
    {
        if (_manager is null)
        {
            return;
        }

        var live = _manager.GetSessions();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in live)
        {
            string id = GetSessionId(session);
            seenIds.Add(id);
            if (_sessions.ContainsKey(id))
            {
                continue;
            }

            session.PlaybackInfoChanged += OnPlaybackChanged;
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _sessions[id] = session;
            SyncSelectorForSession(id);
        }

        foreach (string id in _sessions.Keys.Where(id => !seenIds.Contains(id)).ToList())
        {
            RemoveSessionInternal(id);
        }
    }

    private void SyncSelectorForSession(string id)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            return;
        }

        var status = session.GetPlaybackInfo().PlaybackStatus;
        if (status is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
        {
            _selector.AddSession(id);
            _selector.MarkActive(id);
        }
        else
        {
            _selector.RemoveSession(id);
        }
    }

    private void RemoveSessionInternal(string id)
    {
        if (_sessions.Remove(id, out var session))
        {
            session.PlaybackInfoChanged -= OnPlaybackChanged;
            session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _thumbnails.Remove(id);
            _selector.RemoveSession(id);
        }
    }

    private async Task ReloadCurrentAsync()
    {
        string? id = _selector.Current;
        if (id is null)
        {
            _snapshot = MediaSnapshot.Idle;
            _thumbnails.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_sessions.TryGetValue(id, out var session))
        {
            return;
        }

        string appName = SimplifyAppName(id);
        string title = string.Empty;
        string artist = string.Empty;
        IRandomAccessStreamReference? thumbnailRef = null;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            title = props.Title ?? string.Empty;
            artist = props.Artist ?? string.Empty;
            thumbnailRef = props.Thumbnail;
        }
        catch (Exception ex)
        {
            // Metadata alınamadı (uygulama yanıt vermiyor) — app adı fallback'i kullanılır.
            Debug.WriteLine($"[media] metadata başarısız: {ex.Message}");
        }

        ImageSource? thumb = null;
        if (thumbnailRef is not null && !_thumbnails.TryGetValue(id, out thumb))
        {
            thumb = await TryDecodeThumbnailAsync(thumbnailRef);
            if (thumb is not null)
            {
                _thumbnails[id] = thumb;
            }
        }

        bool isPlaying = false;
        try
        {
            isPlaying = session.GetPlaybackInfo().PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] playback bilgisi alınamadı: {ex.Message}");
        }

        _snapshot = new MediaSnapshot(id, appName, title, artist, isPlaying, thumb);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<ImageSource?> TryDecodeThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream.AsStreamForRead();
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] kapak çözülemedi: {ex.Message}");
            return null;
        }
    }

    /// <summary>AUMID'i okunabilir uygulama adına sadeleştirir: "..."!LastSegment[.exe]</summary>
    private static string SimplifyAppName(string appUserModelId)
    {
        string name = appUserModelId;
        int bang = name.LastIndexOf('!');
        if (bang >= 0 && bang < name.Length - 1)
        {
            name = name[(bang + 1)..];
        }

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>Oturum anahtarı: AUMID (uygulama başına tek oturum). Boşsa "session" fallback.</summary>
    private static string GetSessionId(GlobalSystemMediaTransportControlsSession session)
    {
        string id = session.SourceAppUserModelId;
        return string.IsNullOrWhiteSpace(id) ? "session" : id;
    }

    private void RaiseSessions() => SessionsChanged?.Invoke(this, EventArgs.Empty);

    private void ScheduleRetry()
    {
        _retryTimer?.Stop();
        _retryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer?.Stop();
            _retryTimer = null;
            _ = InitializeAsync();
        };
        _retryTimer.Start();
    }
}
```

- [ ] **Step 2: Derle**

Run: `dotnet build WharfBar.sln`
Expected: temiz derleme. Hata varsa düzelt (ör. eksik `using` — `System.Collections.Generic`, `System.Linq` implicit usings ile gelir; `GlobalSystemMediaTransportControlsSession.GetPlaybackInfo()` senkron döner; `TryGetMediaPropertiesAsync()` doğrudan awaitable).

- [ ] **Step 3: Testler hâlâ geçer mi**

Run: `dotnet test`
Expected: tüm testler geçer (yeni servis test edilemez — WinRT; manuel doğrulama Task 6 sonrası).

- [ ] **Step 4: Commit**

```bash
git add src/WharfBar/Services/MediaSessionService.cs
git commit -m "feat(media): SMTC oturum servisi (snapshot + komutlar + seçici)"
```

---

### Task 4: Durum kataloğu — `media` öğesi + v4 migration

**Files:**
- Modify: `src/WharfBar/Models/TopBarStatusItemIds.cs` (`Media` sabiti, `DefaultOrder`, `EnsureMedia`)
- Modify: `src/WharfBar/Models/AppConfig.cs:85` (`CurrentStatusCatalogVersion` 3 → 4)
- Modify: `src/WharfBar/Services/ConfigService.cs:184-189` (migration bloğuna `EnsureMedia`)
- Test: `tests/WharfBar.Tests/TopBarStatusItemIdsTests.cs` (yeni)

**Interfaces:**
- Consumes: — (mevcut katalog deseni)
- Produces: `TopBarStatusItemIds.Media == "media"`; `DefaultOrder`'da `Brightness`'tan sonra; `EnsureMedia(List<string>)` (eksikse `brightness` sonrası; yoksa `volume` sonrası; o da yoksa sona). Task 6 bu id'yi kullanır.

- [ ] **Step 1: Failing test yaz**

```csharp
using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class TopBarStatusItemIdsTests
{
    [Fact]
    public void DefaultOrder_ContainsMedia_AfterBrightness()
    {
        var order = TopBarStatusItemIds.DefaultOrder.ToList();
        int brightnessIndex = order.IndexOf(TopBarStatusItemIds.Brightness);
        Assert.True(brightnessIndex >= 0);
        Assert.Equal(TopBarStatusItemIds.Media, order[brightnessIndex + 1]);
    }

    [Fact]
    public void Normalize_KeepsMedia_WhenPresent()
    {
        var items = new List<string> { TopBarStatusItemIds.Media, TopBarStatusItemIds.Battery };
        var normalized = TopBarStatusItemIds.Normalize(items);
        Assert.Contains(TopBarStatusItemIds.Media, normalized);
    }

    [Fact]
    public void EnsureMedia_InsertsAfterBrightness()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.Volume,
            TopBarStatusItemIds.Brightness,
            TopBarStatusItemIds.Network,
        };
        TopBarStatusItemIds.EnsureMedia(items);
        Assert.Equal(
            [TopBarStatusItemIds.Volume, TopBarStatusItemIds.Brightness, TopBarStatusItemIds.Media, TopBarStatusItemIds.Network],
            items);
    }

    [Fact]
    public void EnsureMedia_InsertsAfterVolume_WhenBrightnessMissing()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.Volume,
            TopBarStatusItemIds.Network,
        };
        TopBarStatusItemIds.EnsureMedia(items);
        Assert.Equal([TopBarStatusItemIds.Volume, TopBarStatusItemIds.Media, TopBarStatusItemIds.Network], items);
    }

    [Fact]
    public void EnsureMedia_Appends_WhenNeitherPresent()
    {
        var items = new List<string> { TopBarStatusItemIds.DateTime };
        TopBarStatusItemIds.EnsureMedia(items);
        Assert.Equal([TopBarStatusItemIds.DateTime, TopBarStatusItemIds.Media], items);
    }

    [Fact]
    public void EnsureMedia_NoOp_WhenAlreadyPresent()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.Network,
            TopBarStatusItemIds.Media,
            TopBarStatusItemIds.Battery,
        };
        TopBarStatusItemIds.EnsureMedia(items);
        Assert.Equal([TopBarStatusItemIds.Network, TopBarStatusItemIds.Media, TopBarStatusItemIds.Battery], items);
    }
}
```

- [ ] **Step 2: Testi çalıştır — fail beklenir**

Run: `dotnet test`
Expected: `EnsureMedia`/`Media` derleme hatası veya test hataları.

- [ ] **Step 3: Kataloğu güncelle**

`src/WharfBar/Models/TopBarStatusItemIds.cs`:

```csharp
    internal const string Media = "media";
```

`DefaultOrder` (mevcut diziye ekle):

```csharp
        Volume,
        Brightness,
        Media,
        Network,
        Battery,
```

Dosya sonuna (mevcut `EnsureBrightness`'ın ardına):

```csharp
    /// <summary>
    /// Soft migration helper: eksik <see cref="Media"/>'yı <see cref="Brightness"/> sonrasına ekler
    /// (brightness yoksa <see cref="Volume"/> sonrası; o da yoksa sona).
    /// </summary>
    internal static void EnsureMedia(List<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Contains(Media, StringComparer.Ordinal))
        {
            return;
        }

        int index = items.FindIndex(static x =>
            string.Equals(x, Brightness, StringComparison.Ordinal));
        if (index < 0)
        {
            index = items.FindIndex(static x =>
                string.Equals(x, Volume, StringComparison.Ordinal));
        }

        if (index >= 0)
        {
            items.Insert(index + 1, Media);
        }
        else
        {
            items.Add(Media);
        }
    }
```

`src/WharfBar/Models/AppConfig.cs:85`:

```csharp
    internal const int CurrentStatusCatalogVersion = 4;
```

`src/WharfBar/Services/ConfigService.cs` migration bloğu (mevcut iki `Ensure`'in yanına):

```csharp
            TopBarStatusItemIds.EnsureControlCenter(config.TopBar.StatusItems);
            TopBarStatusItemIds.EnsureBrightness(config.TopBar.StatusItems);
            TopBarStatusItemIds.EnsureMedia(config.TopBar.StatusItems);
```

- [ ] **Step 4: Testi çalıştır — pass beklenir**

Run: `dotnet test`
Expected: `TopBarStatusItemIdsTests` geçer (6 test); mevcut testler bozulmadı.

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Models/TopBarStatusItemIds.cs src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/ConfigService.cs tests/WharfBar.Tests/TopBarStatusItemIdsTests.cs
git commit -m "feat(media): durum kataloğu media öğesi + v4 migration"
```

---

### Task 5: `MediaFlyout` görünümü

**Files:**
- Create: `src/WharfBar/Views/MediaFlyout.xaml`
- Create: `src/WharfBar/Views/MediaFlyout.xaml.cs`

**Interfaces:**
- Consumes: `MediaSessionService` (Task 3), `MediaSnapshot` (Task 1), `FlyoutSurface.Create()` (mevcut `Services/FlyoutSurface.cs`)
- Produces (Task 6 kullanır):
  - `internal void Attach(MediaSessionService service)` — abonelik + ilk `Apply`
  - `internal void OnFlyoutOpened()` — `FlyoutRoot.Background = FlyoutSurface.Create()`
  - `public partial class MediaFlyout : UserControl`
  - XAML elemanları: `FlyoutRoot`, `SelectorPanel` (içinde `PreviousSessionButton`, `SessionNameText`, `NextSessionButton`), `NowPlayingPanel` (`ThumbnailImage`, `ThumbnailPlaceholder`, `TitleText`, `ArtistText`), `TransportPanel` (`PreviousButton`, `PlayPauseButton` + `PlayPauseGlyph`, `NextButton`), `EmptyText`

- [ ] **Step 1: XAML'i yaz**

`src/WharfBar/Views/MediaFlyout.xaml`:

```xml
<UserControl x:Class="WharfBar.Views.MediaFlyout"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             MinWidth="320"
             MaxWidth="360"
             Foreground="{DynamicResource TextFillColorPrimaryBrush}">

    <UserControl.Resources>
        <Style x:Key="MediaIconButton" TargetType="Button">
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Padding" Value="8,6" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="FontFamily" Value="{DynamicResource AppIconFontFamily}" />
            <Setter Property="FontSize" Value="16" />
            <Setter Property="Foreground" Value="{DynamicResource TextFillColorPrimaryBrush}" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Bd"
                                Background="{TemplateBinding Background}"
                                CornerRadius="6"
                                Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Bd" Property="Background"
                                        Value="{DynamicResource ControlFillColorSecondaryBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </UserControl.Resources>

    <Border x:Name="FlyoutRoot"
            Background="{DynamicResource ApplicationBackgroundBrush}"
            CornerRadius="12"
            Padding="16"
            Margin="0,0,12,0"
            SnapsToDevicePixels="True">
        <StackPanel>
            <!-- Oturum seçici (yalnızca 1'den fazla oturum varsa) -->
            <Grid x:Name="SelectorPanel" Margin="0,0,0,10" Visibility="Collapsed">
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                    <Button x:Name="PreviousSessionButton"
                            Style="{StaticResource MediaIconButton}"
                            Click="OnPreviousSessionClick"
                            ToolTip="Önceki medya oturumu">
                        <TextBlock FontFamily="{DynamicResource AppIconFontFamily}"
                                   FontSize="12"
                                   Text="&#xE76B;" />
                    </Button>
                    <TextBlock x:Name="SessionNameText"
                               Margin="8,0,8,0"
                               VerticalAlignment="Center"
                               FontSize="12"
                               FontWeight="SemiBold"
                               Opacity="0.75" />
                    <Button x:Name="NextSessionButton"
                            Style="{StaticResource MediaIconButton}"
                            Click="OnNextSessionClick"
                            ToolTip="Sonraki medya oturumu">
                        <TextBlock FontFamily="{DynamicResource AppIconFontFamily}"
                                   FontSize="12"
                                   Text="&#xE76C;" />
                    </Button>
                </StackPanel>
            </Grid>

            <!-- Şu an çalan: kapak + başlık/sanatçı -->
            <Grid x:Name="NowPlayingPanel">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Border Grid.Column="0"
                        Width="56"
                        Height="56"
                        CornerRadius="8"
                        Background="{DynamicResource ControlFillColorDefaultBrush}"
                        VerticalAlignment="Center">
                    <Grid>
                        <Image x:Name="ThumbnailImage"
                               Width="56"
                               Height="56"
                               Stretch="UniformToFill"
                               Visibility="Collapsed" />
                        <TextBlock x:Name="ThumbnailPlaceholder"
                                   FontFamily="{DynamicResource AppIconFontFamily}"
                                   FontSize="22"
                                   HorizontalAlignment="Center"
                                   VerticalAlignment="Center"
                                   Text="&#xE90C;" />
                    </Grid>
                </Border>
                <StackPanel Grid.Column="1" Margin="12,0,0,0" VerticalAlignment="Center">
                    <TextBlock x:Name="TitleText"
                               FontSize="14"
                               FontWeight="SemiBold"
                               TextTrimming="CharacterEllipsis" />
                    <TextBlock x:Name="ArtistText"
                               FontSize="12"
                               Opacity="0.7"
                               Margin="0,2,0,0"
                               TextTrimming="CharacterEllipsis" />
                </StackPanel>
            </Grid>

            <!-- Boş durum -->
            <TextBlock x:Name="EmptyText"
                       Text="Çalan medya yok"
                       FontSize="12"
                       Opacity="0.6"
                       HorizontalAlignment="Center"
                       Margin="0,8,0,0"
                       Visibility="Collapsed" />

            <!-- Taşıma kontrolleri -->
            <StackPanel x:Name="TransportPanel"
                        Orientation="Horizontal"
                        HorizontalAlignment="Center"
                        Margin="0,12,0,0">
                <Button x:Name="PreviousButton"
                        Style="{StaticResource MediaIconButton}"
                        Click="OnPreviousClick"
                        ToolTip="Önceki parça">
                    <TextBlock FontFamily="{DynamicResource AppIconFontFamily}" Text="&#xE892;" />
                </Button>
                <Button x:Name="PlayPauseButton"
                        Style="{StaticResource MediaIconButton}"
                        FontSize="24"
                        Padding="10,6"
                        Click="OnPlayPauseClick"
                        ToolTip="Oynat/Duraklat">
                    <TextBlock x:Name="PlayPauseGlyph"
                               FontFamily="{DynamicResource AppIconFontFamily}"
                               Text="&#xE768;" />
                </Button>
                <Button x:Name="NextButton"
                        Style="{StaticResource MediaIconButton}"
                        Click="OnNextClick"
                        ToolTip="Sonraki parça">
                    <TextBlock FontFamily="{DynamicResource AppIconFontFamily}" Text="&#xE893;" />
                </Button>
            </StackPanel>
        </StackPanel>
    </Border>
</UserControl>
```

- [ ] **Step 2: Code-behind'i yaz**

`src/WharfBar/Views/MediaFlyout.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class MediaFlyout : UserControl
{
    private MediaSessionService? _service;

    public MediaFlyout()
    {
        InitializeComponent();
    }

    internal void Attach(MediaSessionService service)
    {
        if (_service is not null)
        {
            _service.Changed -= OnChanged;
            _service.SessionsChanged -= OnSessionsChanged;
        }

        _service = service;
        _service.Changed += OnChanged;
        _service.SessionsChanged += OnSessionsChanged;
        Apply(_service.Snapshot);
        ApplySessions(_service);
    }

    internal void OnFlyoutOpened()
    {
        FlyoutRoot.Background = FlyoutSurface.Create();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_service is not null)
        {
            Apply(_service.Snapshot);
        }
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        if (_service is not null)
        {
            ApplySessions(_service);
        }
    }

    private void Apply(MediaSnapshot snapshot)
    {
        bool hasSession = snapshot.HasSession;
        NowPlayingPanel.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        TransportPanel.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = hasSession ? Visibility.Collapsed : Visibility.Visible;

        if (!hasSession)
        {
            TitleText.Text = string.Empty;
            ArtistText.Text = string.Empty;
            ThumbnailImage.Visibility = Visibility.Collapsed;
            ThumbnailPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        // Metadata alınamadıysa uygulama adı fallback.
        TitleText.Text = string.IsNullOrEmpty(snapshot.Title) ? snapshot.AppName : snapshot.Title;
        ArtistText.Text = snapshot.Artist;

        if (snapshot.Thumbnail is not null)
        {
            ThumbnailImage.Source = snapshot.Thumbnail;
            ThumbnailImage.Visibility = Visibility.Visible;
            ThumbnailPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            ThumbnailImage.Visibility = Visibility.Collapsed;
            ThumbnailPlaceholder.Visibility = Visibility.Visible;
        }

        PlayPauseGlyph.Text = snapshot.IsPlaying ? "\uE769" : "\uE768";
    }

    private void ApplySessions(MediaSessionService service)
    {
        SelectorPanel.Visibility = service.SessionIds.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        SessionNameText.Text = service.Snapshot.AppName;
    }

    private void OnPreviousSessionClick(object sender, RoutedEventArgs e) => _service?.SelectPrevious();

    private void OnNextSessionClick(object sender, RoutedEventArgs e) => _service?.SelectNext();

    private void OnPreviousClick(object sender, RoutedEventArgs e) => _service?.Previous();

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _service?.TogglePlayPause();

    private void OnNextClick(object sender, RoutedEventArgs e) => _service?.Next();
}
```

- [ ] **Step 3: Derle**

Run: `dotnet build WharfBar.sln`
Expected: temiz derleme (`MediaFlyout` XAML + code-behind eşleşir).

- [ ] **Step 4: Testler**

Run: `dotnet test`
Expected: tüm testler geçer.

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Views/MediaFlyout.xaml src/WharfBar/Views/MediaFlyout.xaml.cs
git commit -m "feat(media): medya flyout'u (kapak/başlık/kontroller/seçici)"
```

---

### Task 6: TopBarWindow kablolaması

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml` (MediaButton + MediaPopup)
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs` (servis, toggle, dinamik görünürlük, dispose)

**Interfaces:**
- Consumes: `MediaSessionService` (Task 3), `MediaFlyout` (Task 5), `TopBarStatusItemIds.Media` (Task 4)
- Produces: çalışan medya öğesi — oturum varken görünür, tıklayınca flyout, kapatma animasyonları, sağ tık menüsü desteği (katalog üzerinden otomatik)

- [ ] **Step 1: XAML — MediaButton ekle**

`src/WharfBar/Views/TopBarWindow.xaml` — `BrightnessButton` bloğu (satır ~172-179) ile `NetworkButton` arasına:

```xml
                <Border x:Name="MediaButton" Style="{StaticResource StatusHit}" Visibility="Collapsed">
                    <Border.ToolTip>
                        <ToolTip Content="Çalan medya" />
                    </Border.ToolTip>
                    <TextBlock x:Name="MediaGlyph"
                               Style="{StaticResource StatusGlyph}"
                               Text="&#xE90B;" />
                </Border>
```

- [ ] **Step 2: XAML — MediaPopup ekle**

`BrightnessPopup` bloğunun ardına (dosya sonuna yakın):

```xml
            <!-- Media Flyout (SMTC şu an çalan) -->
            <Popup x:Name="MediaPopup"
                   Placement="Bottom"
                   PlacementTarget="{Binding ElementName=MediaButton}"
                   StaysOpen="True"
                   AllowsTransparency="True"
                   PopupAnimation="None"
                   VerticalOffset="10"
                   Opened="OnMediaPopupOpened">
                <local:MediaFlyout x:Name="MediaHost" />
            </Popup>
```

- [ ] **Step 3: Code-behind — alanlar + sözlük**

`TopBarWindow.xaml.cs` (satır ~36-64 arası alan bildirimleri):

```csharp
    private MediaSessionService? _media;
    private bool _mediaActive;
    private bool _mediaClosePending;
```

`_statusElements` sözlüğüne (ctor, satır ~79-89):

```csharp
            [TopBarStatusItemIds.Brightness] = BrightnessButton,
            [TopBarStatusItemIds.Media] = MediaButton,
            [TopBarStatusItemIds.Network] = NetworkButton,
```

- [ ] **Step 4: Code-behind — servis kurulumu + tıklama**

`OnSourceInitialized` içinde `_monitorBrightness` bloğunun ardına (satır ~135-136 civarı):

```csharp
        _media = new MediaSessionService();
        _media.Changed += OnMediaChanged;
        _media.SessionsChanged += OnMediaSessionsChanged;
        MediaHost.Attach(_media);
        _ = _media.InitializeAsync();
```

Tıklama kayıtlarına (satır ~141-148):

```csharp
        BrightnessButton.PreviewMouseLeftButtonUp += OnBrightnessClick;
        MediaButton.PreviewMouseLeftButtonUp += OnMediaClick;
```

ESC kablolamasına (satır ~156-157):

```csharp
        QuickSettingsHost.KeyDown += OnFlyoutHostKeyDown;
        BrightnessHost.KeyDown += OnFlyoutHostKeyDown;
        MediaHost.KeyDown += OnFlyoutHostKeyDown;
```

- [ ] **Step 5: Code-behind — flyout aç/kapa**

`OnBrightnessClick`/`ToggleBrightnessFlyout` bloğunun ardına (mevcut yapıya uygun):

```csharp
    private void OnMediaClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleMediaFlyout();
    }

    private void ToggleMediaFlyout()
    {
        if (MediaPopup.IsOpen)
        {
            CloseMedia();
            return;
        }

        CancelPendingClose(MediaHost, ref _mediaClosePending);
        CloseFlyouts();
        MediaPopup.IsOpen = true;
    }

    private void CloseMedia(bool animate = true)
    {
        if (!MediaPopup.IsOpen)
        {
            _mediaClosePending = false;
            return;
        }

        if (animate && !_mediaClosePending)
        {
            _mediaClosePending = true;
            AnimateFlyoutClose(MediaHost, () =>
            {
                _mediaClosePending = false;
                MediaPopup.IsOpen = false;
            });
        }
        else
        {
            CancelPendingClose(MediaHost, ref _mediaClosePending);
            MediaPopup.IsOpen = false;
        }
    }

    private void OnMediaPopupOpened(object sender, EventArgs e)
    {
        CancelPendingClose(MediaHost, ref _mediaClosePending);
        MediaHost.OnFlyoutOpened();
        AnimateFlyoutOpen(MediaHost);
    }
```

`CloseFlyouts` (satır ~504-508) güncelle:

```csharp
    private void CloseFlyouts(bool animate = true)
    {
        CloseQuickSettings(animate);
        CloseBrightness(animate);
        CloseMedia(animate);
    }
```

`OnTopBarPreviewMouseDown` (satır ~165-176) — medyayı kapalı-kontrol ve çapa istisnalarına ekle:

```csharp
        if (!QuickSettingsPopup.IsOpen && !BrightnessPopup.IsOpen && !MediaPopup.IsOpen)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject hit
            && (IsWithin(hit, ControlCenterButton)
                || IsWithin(hit, VolumeButton)
                || IsWithin(hit, NetworkButton)
                || IsWithin(hit, BrightnessButton)
                || IsWithin(hit, MediaButton)))
        {
            return;
        }
```

- [ ] **Step 6: Code-behind — dinamik görünürlük + snapshot**

`ApplyStatusItemLayout` (satır ~679-689) switch'e ekle:

```csharp
                TopBarStatusItemIds.Battery => _batteryPresent,
                TopBarStatusItemIds.Volume => _volumeAvailable,
                TopBarStatusItemIds.Media => _mediaActive,
```

Yeni metotlar (ör. `ApplyStatus`'un yakınına):

```csharp
    private void OnMediaChanged(object? sender, EventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        _mediaActive = _media.HasActiveSession;
        SetPlainToolTip(MediaButton, BuildMediaTooltip(_media.Snapshot));
        ApplyStatusItemLayout();
    }

    private void OnMediaSessionsChanged(object? sender, EventArgs e)
    {
        _mediaActive = _media?.HasActiveSession ?? false;
        ApplyStatusItemLayout();
    }

    private static string BuildMediaTooltip(MediaSnapshot snapshot)
    {
        if (!snapshot.HasSession)
        {
            return "Çalan medya";
        }

        if (string.IsNullOrEmpty(snapshot.Title))
        {
            return snapshot.AppName;
        }

        return string.IsNullOrEmpty(snapshot.Artist)
            ? snapshot.Title
            : $"{snapshot.Title} – {snapshot.Artist}";
    }
```

Not: `WharfBar.Models` using'i dosyada zaten var (satır 10) — ekleme gerekmez.

- [ ] **Step 7: Code-behind — dispose**

`DisposeOwnedServices` (satır ~569-587) sonuna:

```csharp
        _media?.Dispose();
        _media = null;
```

- [ ] **Step 8: Derle + test**

Run: `dotnet build WharfBar.sln`
Run: `dotnet test`
Expected: temiz derleme, tüm testler geçer.

- [ ] **Step 9: Commit**

```bash
git add src/WharfBar/Views/TopBarWindow.xaml src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "feat(media): üst bar medya öğesi + flyout kablolaması"
```

---

### Task 7: Doküman güncellemesi

**Files:**
- Create: `docs/decisions/008-media-panel.md`
- Modify: `docs/01-architecture.md` (dosya listesi, sorumluluk tablosu, yaşam döngüsü, config şeması — `statusCatalogVersion` örneğini `4` yap + `media` notu)
- Modify: `docs/03-development-order.md` (gerekirse medya paneli sırası notu)
- Modify: `.agent/project.md`, `.agent/context/roadmap.md` (özellik ilerlemesi)
- Modify: `.agent/screens/README.md` (tabloya Medya flyout satırı), `.agent/screens/topbar.md` (yeni bölüm)

**Interfaces:** — (doküman, kod yok)

- [ ] **Step 1: ADR yaz**

`docs/decisions/008-media-panel.md` — mevcut ADR formatını izle (ör. `007-veil-overlay.md`). İçerik:
- Karar: Windows SMTC (`GlobalSystemMediaTransportControlsSessionManager`) — yeni bağımlılık yok
- Üst bar öğesi `media`: katalog öğesi + dinamik görünürlük (oturum yokken `Collapsed`)
- Seçim politikası: en son aktif oturum; kapanınca en son aktif kalan
- Çoklu oturum: flyout içinde chevron seçici
- Komutlar: Oynat/Duraklat + İleri/Geri (seek/süre yok — kullanıcı kararı)
- Thread: WinRT olayları → UI Dispatcher; kapak UI thread'de üretilir + `Freeze()`
- Katalog v4 migration

- [ ] **Step 2: Mimari doküman**

`docs/01-architecture.md`:
- Dosya ağacı: `Models/MediaSnapshot.cs`, `Services/MediaSessionService.cs` + `MediaSessionSelector.cs`, `Views/MediaFlyout.xaml(.cs)`
- Sorumluluk tablosuna satırlar: `MediaSessionService` (SMTC izleme + komutlar + seçici), `MediaSessionSelector` (son-aktif politikası), `MediaFlyout` (kapak/başlık/kontroller/seçici), `TopBarWindow` `media` öğesi
- Yaşam döngüsü: TopBarWindow `OnSourceInitialized` → `MediaSessionService` kurulumu; `DisposeOwnedServices` → dispose
- Config şeması örneğinde `statusCatalogVersion: 1` → `4`; `media`'nın `brightness` sonrası eklendiği notu

- [ ] **Step 3: Ekran turu**

`.agent/screens/topbar.md` — durum şeridi tablosuna satır:
`| media | müzik notası glifi E90B | MediaFlyout (kapak + başlık/sanatçı + kontroller + oturum seçici); oturum yokken gizli |`

`.agent/screens/README.md` — tabloya satır:
`| **Medya flyout** | [`topbar.md`](topbar.md#medya-flyout) | SMTC şu an çalan: kapak, başlık/sanatçı, Oynat/Duraklat + İleri/Geri, çoklu oturum seçici; öğe yalnızca oturum aktifken görünür |`

- [ ] **Step 4: Proje bağlamı**

`.agent/project.md` ve `.agent/context/roadmap.md` — mevcut stile uygun özellik notu (medya paneli tamamlandı; SMTC, dinamik öğe, flyout).

- [ ] **Step 5: Derle + test (doküman sonrası genel doğrulama)**

Run: `dotnet build WharfBar.sln`
Run: `dotnet test`
Expected: temiz.

- [ ] **Step 6: Commit**

```bash
git add docs/decisions/008-media-panel.md docs/01-architecture.md docs/03-development-order.md .agent/
git commit -m "docs(media): ADR-008, mimari ve UI turu güncellemeleri"
```

---

## Manuel doğrulama (tüm plan sonrası — app çalıştır)

1. `dotnet run --project src/WharfBar`
2. Spotify / tarayıcıda müzik çal → `media` öğesi belirir (brightness sonrası), tooltip "Başlık – Sanatçı"
3. Tıkla → flyout: kapak, başlık, sanatçı; Oynat/Duraklat (glif canlı değişir), İleri/Geri parça değiştirir
4. Müziği durdur / uygulamayı kapat → öğe kaybolur; flyout açıksa "Çalan medya yok"
5. Spotify + tarayıcı aynı anda çalıyor → seçici görünür; chevron oturum değiştirir, kontroller doğru uygulamaya gider
6. Dış tıklama / ESC / öğeye tekrar tıklama → animasyonlu kapanır
7. Sağ tık menüsü: `media` onay kutusu → kalıcı gizleme/gösterme; yeniden başlatınca korunur
8. Mevcut config ile açılış → `media` otomatik `brightness` sonrasına eklenir (katalog v4)
9. Quit → hata yok; medya uygulamaları etkilenmez
10. Çıkış sonrası `explorer.exe` restart gerekmiyor (AppBar disiplini bozulmadı)
