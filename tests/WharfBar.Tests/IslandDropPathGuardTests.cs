using System;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Security boundary for drop-copy paths. Because <c>island.json</c> can be edited by hand,
/// <c>FilePath</c> is untrusted; no path outside <c>Drop</c> may ever be deleted.
/// Opening has a separate gate (<c>CanOpenShelfFile</c>): the <c>Drop</c> prefix is not
/// required; a missing path, a device path, a folder with a reparse point and a junction
/// escape are rejected; a real file and a real folder are accepted (the shelf also hosts
/// folders). The tests run against the real file system, because the actual danger
/// (a junction leading outside Drop) is only visible on disk.
/// </summary>
public sealed class IslandDropPathGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "WharfBarTests", "dropguard-" + Guid.NewGuid().ToString("N"));

    private string DropRoot => Path.Combine(_root, "Drop");

    private string Prefix => DropRoot + Path.DirectorySeparatorChar;

    public IslandDropPathGuardTests()
    {
        Directory.CreateDirectory(DropRoot);
    }

    public void Dispose()
    {
        try
        {
            // Junctions may follow their targets when deleted; clean up only our own tree.
            foreach (string dir in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(dir);
                }
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary folder; a failed cleanup does not affect the test result.
        }
    }

    private string WriteFile(string relative, string content)
    {
        string full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void IsInsideDropPrefix_AcceptsAFileUnderDrop()
    {
        Assert.True(IslandService.IsInsideDropPrefix(
            Path.Combine(DropRoot, "a.txt"), Prefix, out string full));
        Assert.Equal(Path.Combine(DropRoot, "a.txt"), full);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsInsideDropPrefix_RejectsEmptyInput(string? path)
    {
        Assert.False(IslandService.IsInsideDropPrefix(path, Prefix, out string full));
        Assert.Equal(string.Empty, full);
    }

    /// <summary>Sibling folder: "…\DropYedek" must not count as under "…\Drop" (separator requirement).</summary>
    [Fact]
    public void IsInsideDropPrefix_RejectsSiblingFolderWithDropPrefix()
    {
        string sibling = Path.Combine(_root, "DropYedek", "a.txt");
        Assert.False(IslandService.IsInsideDropPrefix(sibling, Prefix, out _));
    }

    [Fact]
    public void IsInsideDropPrefix_RejectsAnythingOutsideDrop()
    {
        string outside = WriteFile(Path.Combine("Documents", "tehlikeli.docx"), "user data");
        Assert.False(IslandService.IsInsideDropPrefix(outside, Prefix, out _));
    }

    [Fact]
    public void IsInsideDropPrefix_RejectsTraversalOutOfDrop()
    {
        string escape = Path.Combine(DropRoot, "..", "Documents", "tehlikeli.docx");
        Assert.False(IslandService.IsInsideDropPrefix(escape, Prefix, out _));
    }

    [Fact]
    public void AllDirectoryComponentsAreReal_True_ForPlainSubfolders()
    {
        Directory.CreateDirectory(Path.Combine(DropRoot, "2026", "10"));
        string file = Path.Combine(DropRoot, "2026", "10", "rapor.docx");
        File.WriteAllText(file, "x");

        Assert.True(IslandService.AllDirectoryComponentsAreReal(Prefix, file, out string? offender));
        Assert.Null(offender);
    }

    /// <summary>Drop itself does not count as a component — the user may not have put a link there.</summary>
    [Fact]
    public void AllDirectoryComponentsAreReal_IgnoresTheRootItself()
    {
        string file = Path.Combine(DropRoot, "a.txt");
        File.WriteAllText(file, "x");

        Assert.True(IslandService.AllDirectoryComponentsAreReal(Prefix, file, out _));
    }

    /// <summary>
    /// The core of O3: if <c>Drop\baglanti\</c> is a junction, the path passes the prefix test,
    /// but <c>File.Delete</c> follows it and irreversibly deletes the file OUTSIDE Drop. The scan
    /// path (<c>DeleteDropFolderContents</c>) already prevents this with
    /// <c>AttributesToSkip = … | ReparsePoint</c>; the record path must have the same protection.
    /// </summary>
    [Fact]
    public void AllDirectoryComponentsAreReal_False_ForJunctionInsideDrop()
    {
        string outside = Path.Combine(_root, "Outside");
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "tehlikeli.docx");
        File.WriteAllText(victim, "important");

        string link = Path.Combine(DropRoot, "baglanti");
        Assert.True(CreateJunction(link, outside), "mklink /J başarısız — koruma test edilemiyor");

        string throughLink = Path.Combine(link, "tehlikeli.docx");
        Assert.True(File.Exists(throughLink), "junction hedefe ulaşmıyor");

        // The prefix check counts this path as "under Drop" — so it is not sufficient on its own.
        Assert.True(IslandService.IsInsideDropPrefix(throughLink, Prefix, out _));

        Assert.False(IslandService.AllDirectoryComponentsAreReal(Prefix, throughLink, out string? offender));
        Assert.Equal(link, offender);
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void AllDirectoryComponentsAreReal_False_ForJunctionDeeperInDrop()
    {
        string outside = Path.Combine(_root, "Outside2");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "v.txt"), "important");

        string nested = Path.Combine(DropRoot, "2026");
        Directory.CreateDirectory(nested);
        string link = Path.Combine(nested, "baglanti");
        Assert.True(CreateJunction(link, outside), "mklink /J başarısız");

        string throughLink = Path.Combine(link, "v.txt");
        Assert.False(IslandService.AllDirectoryComponentsAreReal(Prefix, throughLink, out string? offender));
        Assert.Equal(link, offender);
    }

    /// <summary>A missing component is not "dangerous"; the file itself is ignored anyway.</summary>
    [Fact]
    public void AllDirectoryComponentsAreReal_True_WhenAComponentIsMissing()
    {
        string missing = Path.Combine(DropRoot, "yok", "a.txt");
        Assert.True(IslandService.AllDirectoryComponentsAreReal(Prefix, missing, out _));
    }

    /// <summary>
    /// The gate accepts a real file and a real folder (the shelf also hosts folders; a folder
    /// is opened in Explorer via <c>UseShellExecute</c>). Rejection of a folder with a reparse point
    /// is covered in <see cref="CanOpenShelfFile_RejectsJunctionEscape"/> — the junction is set up there.
    /// </summary>
    [Fact]
    public void CanOpenShelfFile_AcceptsRegularFileAndRealFolder_RejectsMissingAndDevice()
    {
        string file = WriteFile("raf.txt", "shelf");
        string legacy = WriteFile(Path.Combine("Drop", "legacy.txt"), "old copy");
        string directory = Path.Combine(_root, "bir-klasor");
        Directory.CreateDirectory(directory);
        string missing = Path.Combine(_root, "yok.txt");

        WithIsland(service =>
        {
            Assert.True(service.CanOpenShelfFile(file));
            Assert.False(IslandService.IsInsideDropPrefix(file, Prefix, out _));

            // A legacy Drop copy is an ordinary file too; opening does not require the Drop prefix,
            // but being inside the prefix is not an obstacle either.
            Assert.True(service.CanOpenShelfFile(legacy));

            Assert.True(service.CanOpenShelfFile(directory));
            Assert.False(service.CanOpenShelfFile(missing));
            Assert.False(service.CanOpenShelfFile(null));
            Assert.False(service.CanOpenShelfFile(string.Empty));
            Assert.False(service.CanOpenShelfFile("   "));
            Assert.False(service.CanOpenShelfFile(@"\\.\NUL"));
            Assert.False(service.CanOpenShelfFile("NUL"));

            Assert.True(File.Exists(file));
            Assert.True(Directory.Exists(directory));
        });
    }

    /// <summary>
    /// The junction passes the prefix test, but opening follows the target. The gate rejects it
    /// and does not touch the target file.
    /// </summary>
    [Fact]
    public void CanOpenShelfFile_RejectsJunctionEscape()
    {
        string outside = Path.Combine(_root, "OutsideOpen");
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "tehlikeli.docx");
        File.WriteAllText(victim, "important");

        string link = Path.Combine(DropRoot, "baglanti-open");
        Assert.True(CreateJunction(link, outside), "mklink /J başarısız — koruma test edilemiyor");

        string throughLink = Path.Combine(link, "tehlikeli.docx");
        Assert.True(File.Exists(throughLink), "junction hedefe ulaşmıyor");
        Assert.True(IslandService.IsInsideDropPrefix(throughLink, Prefix, out _));

        WithIsland(service =>
        {
            Assert.False(service.CanOpenShelfFile(throughLink));
            Assert.False(service.CanOpenShelfFile(link));
        });

        Assert.True(File.Exists(victim));
        Assert.Equal("important", File.ReadAllText(victim));
    }

    private void WithIsland(Action<IslandService> body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            IslandService? service = null;
            try
            {
                service = new IslandService(
                    Dispatcher.CurrentDispatcher,
                    new IslandStore(_root),
                    new IslandConfig
                    {
                        Enabled = false,
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
                    // If the dispatcher was never created, there is nothing to shut down.
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA test thread did not finish");
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }

    /// <summary>Creating a junction does not require administrator rights (mklink /J).</summary>
    private static bool CreateJunction(string linkPath, string targetPath)
    {
        try
        {
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (mklink is null)
            {
                return false;
            }

            mklink.WaitForExit(10_000);
            return mklink.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
