using System.Diagnostics;
using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockFolderListingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "WharfBarTests", "folderstack-" + Guid.NewGuid().ToString("N"));

    public DockFolderListingTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
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
        }
    }

    [Fact]
    public void Select_SkipsReparseAndUnreadable_AndCapsRows()
    {
        var candidates = new List<DockFolderCandidate>
        {
            new("link", @"C:\a\link", true, true, true),
            new("gone", @"C:\a\gone", false, false, false),
            new("notes", @"C:\a\notes", true, true, false),
            new("b.txt", @"C:\a\b.txt", false, true, false),
            new("a.txt", @"C:\a\a.txt", false, true, false),
        };
        for (int i = 0; i < 20; i++)
        {
            candidates.Add(new($"f{i:00}.txt", $@"C:\a\f{i:00}.txt", false, true, false));
        }

        DockFolderPage page = DockFolderListing.Select(candidates);
        Assert.Equal(DockFolderListing.MaxVisibleRows, page.Entries.Count);
        Assert.True(page.Truncated);
        Assert.Equal("notes", page.Entries[0].Name);
        Assert.DoesNotContain(page.Entries, entry => entry.Name is "link" or "gone");
        Assert.True(page.Entries[0].IsDirectory);
    }

    [Fact]
    public void TryRead_DoesNotEnterAReparseDirectory()
    {
        string target = Path.Combine(_root, "target");
        string link = Path.Combine(_root, "link");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "secret.txt"), "no");
        Assert.True(CreateJunction(link, target), "mklink /J başarısız — koruma test edilemiyor");

        Assert.False(DockFolderListing.TryRead(link, out DockFolderPage page));
        Assert.Empty(page.Entries);
        Assert.True(File.Exists(Path.Combine(target, "secret.txt")));
    }

    [Fact]
    public void TryRead_SkipsAReparseChild()
    {
        string folder = Path.Combine(_root, "stack");
        string outside = Path.Combine(_root, "outside");
        string link = Path.Combine(folder, "jump");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "yes");
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "no");
        Assert.True(CreateJunction(link, outside), "mklink /J başarısız — koruma test edilemiyor");

        Assert.True(DockFolderListing.TryRead(folder, out DockFolderPage page));
        Assert.Equal(["keep.txt"], page.Entries.Select(entry => entry.Name).ToArray());
        Assert.True(File.Exists(Path.Combine(outside, "secret.txt")));
    }

    private static bool CreateJunction(string linkPath, string targetPath)
    {
        using Process? mklink = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        if (mklink is null)
        {
            return false;
        }

        mklink.WaitForExit(10_000);
        return mklink.ExitCode == 0;
    }
}
