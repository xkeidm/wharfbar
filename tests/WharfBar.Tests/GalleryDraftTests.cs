using System.Diagnostics;
using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class GalleryDraftTests
{
    [Fact]
    public void NewlyConnected_SkipsTheFirstPoll()
    {
        var current = new[] { new BluetoothDeviceInfo("a", "Kulaklık", true) };
        Assert.Empty(BluetoothConnect.NewlyConnected(null, current, seeded: false));
    }

    [Fact]
    public void NewlyConnected_NamesOnlyTheNewDevice()
    {
        var previous = new[]
        {
            new BluetoothDeviceInfo("a", "Kulaklık", true),
            new BluetoothDeviceInfo("b", "Fare", false),
        };
        var current = new[]
        {
            new BluetoothDeviceInfo("a", "Kulaklık", true),
            new BluetoothDeviceInfo("b", "Fare", true),
        };

        IReadOnlyList<string> names = BluetoothConnect.NewlyConnected(previous, current, seeded: true);
        Assert.Equal(["Fare"], names);
    }

    [Fact]
    public void SameDevices_IgnoresAnUnchangedPairedList()
    {
        var devices = new[] { new BluetoothDeviceInfo("a", "Kulaklık", true) };
        Assert.True(BluetoothService.SameDevices(devices, devices));
        Assert.False(BluetoothService.SameDevices(
            devices,
            [new BluetoothDeviceInfo("a", "Kulaklık", false)]));
    }

    [Fact]
    public void Heights_AreFlatWhenThePeakIsZero()
    {
        VisualizerBars.Reset();
        Assert.Equal([1, 1, 1, 1, 1], VisualizerBars.Heights(0f));
    }

    [Fact]
    public void Heights_FollowThePeak()
    {
        VisualizerBars.Reset();
        int[] heights = VisualizerBars.Heights(1f);
        Assert.Equal(5, heights.Length);
        Assert.True(heights[2] >= heights[0]);
        Assert.True(heights[2] > 1);
    }

    [Fact]
    public void Heights_MoveOnQuietButAudiblePeaks()
    {
        VisualizerBars.Reset();
        // Shared-mode meters often sit around 0.15 while music is playing.
        int[] quiet = VisualizerBars.Heights(0.15f);
        Assert.True(quiet[2] >= 6, $"center bar was {quiet[2]}");
        Assert.True(quiet[2] > quiet[0]);
    }

    [Fact]
    public void Heights_ReleaseAtDifferentRates()
    {
        VisualizerBars.Reset();
        _ = VisualizerBars.Heights(1f);
        int[] after = [1, 1, 1, 1, 1];
        for (int i = 0; i < 4; i++)
        {
            after = VisualizerBars.Heights(0f);
        }

        // Center decays fastest; edge bars hold longer — motion from one peak.
        Assert.True(after[0] > after[2], $"edge={after[0]} center={after[2]}");
        Assert.True(after[4] > after[2], $"edge={after[4]} center={after[2]}");
    }

    [Fact]
    public void Filter_MatchesTheSearch()
    {
        var apps = new[]
        {
            new LaunchpadApp("Not Defteri", @"C:\n.lnk"),
            new LaunchpadApp("Kod", @"C:\k.lnk"),
        };

        IReadOnlyList<LaunchpadApp> found = LaunchpadCatalog.Filter(apps, "not");
        Assert.Single(found);
        Assert.Equal("Not Defteri", found[0].Name);
    }

    [Theory]
    [InlineData(null, "win11")]
    [InlineData("", "win11")]
    [InlineData("nope", "win11")]
    [InlineData("WIN11", "win11")]
    [InlineData("macos", "mac")]
    [InlineData("mac", "mac")]
    [InlineData("windows8", "win8")]
    [InlineData("win8", "win8")]
    public void Style_NormalizesToAKnownFace(string? raw, string expected)
    {
        Assert.Equal(expected, LaunchpadStyles.Normalize(raw));
    }

    [Fact]
    public void Scan_KeepsShortcutsBesideAnInaccessibleJunction()
    {
        string root = Path.Combine(Path.GetTempPath(), "wharfbar-lp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string junction = Path.Combine(root, "Programlar");
        try
        {
            File.WriteAllText(Path.Combine(root, "Alpha.lnk"), string.Empty);
            string programs = Path.Combine(root, "Programs");
            Directory.CreateDirectory(programs);
            File.WriteAllText(Path.Combine(programs, "Nested.lnk"), string.Empty);
            Assert.True(TryPlantDeniedJunction(junction));

            IReadOnlyList<LaunchpadApp> apps = LaunchpadCatalog.Scan(root);

            Assert.Contains(apps, app => app.Name == "Alpha");
            Assert.Contains(apps, app => app.Name == "Nested");
        }
        finally
        {
            try
            {
                Directory.Delete(junction);
            }
            catch (DirectoryNotFoundException)
            {
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void List_ReadsTheRealStartMenu()
    {
        IReadOnlyList<LaunchpadApp> apps = LaunchpadCatalog.List();
        Assert.NotEmpty(apps);
        Assert.All(apps, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            Assert.EndsWith(".lnk", app.ShortcutPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool TryPlantDeniedJunction(string junction)
    {
        // Every NTFS Windows volume has this directory, and listing it is denied.
        // A junction makes AllDirectories throw the same UnauthorizedAccessException
        // the localized Start Menu\Programlar reparse throws on a Turkish install.
        using Process? link = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junction}\" \"C:\\System Volume Information\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        if (link is null || !link.WaitForExit(5000))
        {
            return false;
        }

        return link.ExitCode == 0;
    }
}
