using System;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Source of "take from clipboard": the newest plain-text record in the service history. Seeded records
/// are supplied through an initialized store (the AddEntry STA worker path cannot be used in the test
/// environment); the real clipboard is not touched, there is no OS write.
/// </summary>
public sealed class IslandServiceTextPickTests
{
    [Fact]
    public void TryLastClipboardText_EmptyHistory_IsFalse()
    {
        Run(service =>
        {
            bool picked = service.TryLastClipboardText(out string text);

            Assert.False(picked);
            Assert.Equal(string.Empty, text);
        });
    }

    [Fact]
    public void TryLastClipboardText_NewestTextWins()
    {
        Run(service =>
        {
            bool picked = service.TryLastClipboardText(out string text);

            Assert.True(picked);
            Assert.Equal("yeni metin", text);
        }, SeedTwoTextEntries);
    }

    private static IslandEntry[] SeedTwoTextEntries() => new[]
    {
        new IslandEntry(1, IslandEntryKind.Text, "eski metin", null, DateTime.UtcNow.AddMinutes(-2), null),
        new IslandEntry(2, IslandEntryKind.Text, "yeni metin", null, DateTime.UtcNow.AddMinutes(-1), null),
    };

    private static void Run(Action<IslandService> body, Func<IslandEntry[]>? seed = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "WharfBarTests", "prompterpick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            IslandService? service = null;
            try
            {
                var store = new IslandStore(root);
                if (seed is not null)
                {
                    store.Save(seed());
                }

                service = new IslandService(
                    Dispatcher.CurrentDispatcher,
                    store,
                    new IslandConfig
                    {
                        Enabled = true,
                        Persistence = IslandConfig.PersistencePersistent,
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
