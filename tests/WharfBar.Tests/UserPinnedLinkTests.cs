using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class UserPinnedLinkTests
{
    [Fact]
    public void ChoosesALink_InsideThePinDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wharf-pin-rules");
        bool chosen = UserPinnedLink.TryChoosePath(
            dir,
            @"C:\Windows\System32\notepad.exe",
            new Dictionary<string, string>(),
            out string link);

        Assert.True(chosen);
        Assert.Equal(Path.Combine(dir, "notepad.lnk"), link);
        Assert.True(UserPinnedLink.IsInside(dir, link));
    }

    [Fact]
    public void ReusesTheSameShortcut_WhenTheTargetMatches()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wharf-pin-rules");
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["notepad.lnk"] = @"C:\Windows\System32\notepad.exe",
        };

        Assert.True(UserPinnedLink.TryChoosePath(dir, @"C:\Windows\System32\notepad.exe", existing, out string link));
        Assert.EndsWith("notepad.lnk", link, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoesNotOverwrite_ADifferentTarget()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wharf-pin-rules");
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["notepad.lnk"] = @"C:\Windows\System32\calc.exe",
        };

        Assert.True(UserPinnedLink.TryChoosePath(dir, @"C:\Windows\System32\notepad.exe", existing, out string link));
        Assert.EndsWith("notepad (2).lnk", link, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAPath_ThatLeavesTheDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wharf-pin-rules");
        Assert.False(UserPinnedLink.IsInside(dir, Path.Combine(dir, "..", "escape.lnk")));
        Assert.False(UserPinnedLink.IsInside(dir, Path.Combine(dir, "notes.txt")));
    }

    [Fact]
    public void WriteAndRemove_StayInsideATempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wharf-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string target = Environment.GetEnvironmentVariable("SystemRoot") is { Length: > 0 } root
            ? Path.Combine(root, "System32", "notepad.exe")
            : @"C:\Windows\System32\notepad.exe";
        try
        {
            Assert.True(File.Exists(target));
            Assert.True(UserPinnedWriter.TryPin(dir, target, string.Empty, "Not Defteri"));
            string lnk = Assert.Single(Directory.EnumerateFiles(dir, "*.lnk"));
            Assert.True(UserPinnedLink.IsInside(dir, lnk));
            Assert.True(ShortcutResolver.TryResolve(lnk, out string resolved, out _, out _));
            Assert.True(UserPinnedLink.SameTarget(resolved, target));
            Assert.True(UserPinnedWriter.TryUnpin(dir, target));
            Assert.Empty(Directory.EnumerateFiles(dir, "*.lnk"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
