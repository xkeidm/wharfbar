using System.IO;
using System.Threading;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The <see cref="IslandService.PutOnClipboard"/> decision layer. The actual clipboard write happens on
/// an STA worker and is not tested in CI (the native clipboard writer is a black box); what is tested
/// is the behavior: the gates (island disabled, empty text, Dispose) report the result as <c>false</c>,
/// add no record to the list and cannot raise <see cref="IslandService.Changed"/>.
/// If the UI dispatcher is gone, the notification is silently dropped.
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

            // No write is attempted from a disabled island: the result is reported on the same call (we are on the UI thread).
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
            // Passing through the disabled path with a null completed callback returns without throwing.
            service.PutOnClipboard("#90C7FF");
            service.PutOnClipboard("");
        });
    }

    [Fact]
    public void PutOnClipboard_AfterDispose_ReportsFailureWithoutWriteAttempt()
    {
        // The island is set up as enabled so that the Dispose gate engages more than once:
        // with a disabled island no write is attempted anyway, so the Dispose information could not be tested.
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

            // The gate reports its result: a single call, false. A later notification (if CanPostToUi
            // has closed) is silently dropped — the contract is "exactly one notification while the UI is alive".
            Assert.Equal(1, reports);
            Assert.False(reported);
        }, enabled: true);
    }

    [Fact]
    public void PutBack_DisabledIsland_DoesNotWriteOrReport()
    {
        // PutBack behavior guard: PutOnClipboard was added, but the PutBack path must stay the same —
        // a text record is silently dropped on a disabled island, there is no result notification (the signature is the old one).
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
    /// CF_HDROP round trip. The file list that the "Copy file" menu action writes to the clipboard
    /// must be readable back from the real clipboard: the write uses <c>DROPFILES { pFiles = offsetof(char data),
    /// fWide = 1 }</c> plus a double-null-terminated UTF-16 list, and the read mirrors the same walk.
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

                // OpenClipboard can fail while another process holds the clipboard
                // (CLIPBRD_E_CANT_OPEN) — a transient condition, hence a few retries.
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
                // Do not leave temp files that are about to be deleted on the clipboard: overwrite the list by writing text.
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
    /// STA thread for tests that touch the clipboard. Calls to <see cref="ClipboardNative"/>
    /// are made from an STA worker thread (repository rule); xunit's own thread is MTA.
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
