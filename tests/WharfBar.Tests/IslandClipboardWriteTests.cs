using System.IO;
using System.Threading;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// <see cref="IslandService.PutOnClipboard"/> karar katmanı. Gerçek pano yazımı STA
/// worker'da olur ve CI'da test edilmez (native clipboard yazarı kara kutu); test edilen
/// davranış: kapılar (kapalı island, boş metin, Dispose) sonucu <c>false</c> olarak
/// bildirir, listeye kayıt eklemez ve <see cref="IslandService.Changed"/> kabartamaz.
/// UI dispatcher gitmişse bildirim sessizce düşer.
/// </summary>
public sealed class IslandClipboardWriteTests
{
    [Fact]
    public void PutOnClipboard_WhenDisabled_ReportsFailureWithoutAddingRecord()
    {
        Run(service =>
        {
            int changed = 0;
            service.Changed += (_, _) => changed++;

            bool? reported = null;
            int reports = 0;
            service.PutOnClipboard("#90C7FF", ok =>
            {
                reports++;
                reported = ok;
            });

            // Kapalı adadan yazma denenmez: sonuç aynı çağrıda bildirilir (UI thread'indeyiz).
            Assert.Equal(1, reports);
            Assert.False(reported);
            Assert.Empty(service.Entries);
            Assert.Equal(0, changed);
        });
    }

    [Fact]
    public void PutOnClipboard_EmptyText_ReportsFailureWithoutDispatch()
    {
        Run(service =>
        {
            foreach (string? text in new[] { null, "" })
            {
                bool? reported = null;
                int reports = 0;
                service.PutOnClipboard(text!, ok =>
                {
                    reports++;
                    reported = ok;
                });

                Assert.Equal(1, reports);
                Assert.False(reported);
            }

            Assert.Empty(service.Entries);
        });
    }

    [Fact]
    public void PutOnClipboard_NullCompleteCallback_AcceptedOnGates()
    {
        Run(service =>
        {
            // completed null iken kapalı yoldan geçmek fırlatmadan çıkar.
            service.PutOnClipboard("#90C7FF");
            service.PutOnClipboard("");
        });
    }

    [Fact]
    public void PutOnClipboard_AfterDispose_ReportsFailureWithoutWriteAttempt()
    {
        // Dispose kapısını bir kereden fazla devreye sokmak için island açık kurulur:
        // kapalı island zaten hiç yazma denemeyeceği için Dispose bilgisi test edilemezdi.
        Run(service =>
        {
            service.Dispose();

            bool? reported = null;
            int reports = 0;
            service.PutOnClipboard("#90C7FF", ok =>
            {
                reports++;
                reported = ok;
            });

            // Kapı sonucu bildirir: tek çağrı, false. Sonraki bildirim (CanPostToUi
            // kapanırsa) sessiz düşer — sözleşme "UI yaşıyorken tam bir bildirim".
            Assert.Equal(1, reports);
            Assert.False(reported);
        }, enabled: true);
    }

    [Fact]
    public void PutBack_DisabledIsland_DoesNotWriteOrReport()
    {
        // PutBack davranış koruması: PutOnClipboard eklendi ama PutBack yolu aynı kalmalı —
        // metin kaydı kapalı adada sessizce düşer, sonuç bildirimi yoktur (signature eski).
        Run(service =>
        {
            var entry = new IslandEntry(1, IslandEntryKind.Text, "keep-out", null, DateTime.UtcNow, null);

            int changed = 0;
            service.Changed += (_, _) => changed++;

            service.PutBack(entry);

            Assert.Empty(service.Entries);
            Assert.Equal(0, changed);
        });
    }

    /// <summary>
    /// CF_HDROP gidiş-dönüşü. "Dosyayı kopyala" menü eyleminin panoya yazdığı dosya listesi
    /// gerçek panodan okunabilmeli: yazma <c>DROPFILES { pFiles = offsetof(char data),
    /// fWide = 1 }</c> + çift null'lu UTF-16 listesi, okuma aynı yürüyüşün simetriği.
    /// </summary>
    [Fact]
    public void FileDropList_UserCopyIsReadBack() =>
        RunSta(() =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "WharfBarTests", "fdl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string a = Path.Combine(dir, "a.txt");
            string b = Path.Combine(dir, "b.txt");
            try
            {
                File.WriteAllText(a, "1");
                File.WriteAllText(b, "2");

                // Pano başka bir süreçte kilitliyken OpenClipboard başarısız olabilir
                // (CLIPBRD_E_CANT_OPEN) — geçici bir durum, bu yüzden birkaç deneme yapılır.
                bool wrote = false;
                bool read = false;
                List<string> paths = [];
                for (int attempt = 0; attempt < 5 && !read; attempt++)
                {
                    wrote = ClipboardNative.TryWriteFileDropList([a, b]);
                    read = wrote && ClipboardNative.TryReadFileDropList(out paths);
                    if (!read)
                    {
                        Thread.Sleep(50);
                    }
                }

                Assert.True(wrote, "CF_HDROP write failed on every attempt");
                Assert.True(read, "CF_HDROP read-back failed on every attempt");
                Assert.Equal(2, paths.Count);
                Assert.Contains(paths, p => p.TrimEnd('\\') == Path.GetFullPath(a));
                Assert.Contains(paths, p => p.TrimEnd('\\') == Path.GetFullPath(b));
            }
            finally
            {
                // Silinecek geçici dosyaları panoda bırakma: metin yazarak listenin üzerine geç.
                _ = ClipboardNative.TryWriteText("wharfbar clipboard test");
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        });

    private static void Run(Action<IslandService> body, bool enabled = false)
    {
        string root = Path.Combine(Path.GetTempPath(), "WharfBarTests", "clipwrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            IslandService? service = null;
            try
            {
                var store = new IslandStore(root);
                service = new IslandService(
                    Dispatcher.CurrentDispatcher,
                    store,
                    new IslandConfig
                    {
                        Enabled = enabled,
                        Persistence = IslandConfig.PersistenceSession,
                    });
                body(service);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                service?.Dispose();
                try
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
                catch (InvalidOperationException)
                {
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA test thread did not finish");

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }

    /// <summary>
    /// Panoya dokunan testler için STA iş parçacığı. <see cref="ClipboardNative"/> çağrıları
    /// STA worker thread'inden yapılır (depo kuralı); xunit'in kendi thread'i MTA'dır.
    /// </summary>
    private static void RunSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA clipboard test thread did not finish");

        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }
}
