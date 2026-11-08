using System;
using System.IO;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// A save that cannot reach the disk (read-only profile, or a restricted launch whose low-integrity
/// token cannot write into the medium-integrity %AppData%) must never escape into a UI handler:
/// on 2026-11-08 the island hub-tab click crashed the whole app through exactly this path.
/// </summary>
public sealed class ConfigServiceSaveFailureTests
{
    [Fact]
    public void Save_UnwritableDirectory_DoesNotThrow_AndKeepsInMemoryConfig()
    {
        string root = NewTempDirectory();
        try
        {
            // A file where the config directory should be: CreateDirectory throws IOException.
            string blocker = Path.Combine(root, "blocker");
            File.WriteAllText(blocker, "not a directory");

            var service = new ConfigService(Path.Combine(blocker, "nested"));
            AppConfig config = ConfigService.CreateDefault();
            config.Theme = AppConfig.ThemeDark;

            Exception? thrown = Record.Exception(() => service.Save(config));

            Assert.Null(thrown);
            Assert.NotNull(service.LastSaveError);
            Assert.Equal(AppConfig.ThemeDark, service.Current.Theme);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_WritableDirectory_ClearsTheFailure_AndWritesTheFile()
    {
        string root = NewTempDirectory();
        try
        {
            var service = new ConfigService(root);
            AppConfig config = ConfigService.CreateDefault();

            Exception? thrown = Record.Exception(() => service.Save(config));

            Assert.Null(thrown);
            Assert.Null(service.LastSaveError);
            Assert.True(File.Exists(service.ConfigPath));
            Assert.False(File.Exists(service.ConfigPath + ".tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "wharfbar-config-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
