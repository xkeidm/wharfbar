using System.IO;
using System.Threading;
using System.Windows.Threading;
using WharfBar.Models;
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
}
