using System.Diagnostics;
using System.IO;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockRecyclePolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "WharfBarTests", "recycle-" + Guid.NewGuid().ToString("N"));

    public DockRecyclePolicyTests()
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

    [Theory]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, false, false, true)]
    public void CanRecycle_RejectsMissingUnreadableAndReparseDirectories(
        bool exists,
        bool readable,
        bool directory,
        bool reparse,
        bool expected)
    {
        Assert.Equal(expected, DockRecyclePolicy.CanRecycle(exists, readable, directory, reparse));
    }

    [Fact]
    public void ApiSucceeded_RequiresZeroAndNoAbort()
    {
        Assert.True(DockRecyclePolicy.ApiSucceeded(0, aborted: false));
        Assert.False(DockRecyclePolicy.ApiSucceeded(0, aborted: true));
        Assert.False(DockRecyclePolicy.ApiSucceeded(1, aborted: false));
        Assert.False(DockRecyclePolicy.ApiSucceeded(0x75, aborted: false));
    }

    [Fact]
    public void DeleteFlags_AreRecycleNotPermanent()
    {
        Assert.Equal(0x0003u, RecycleBinNative.FO_DELETE);
        Assert.Equal((ushort)0x0040, RecycleBinNative.FOF_ALLOWUNDO);
        if (Environment.Is64BitProcess)
        {
            Assert.Equal(56, RecycleBinNative.FileOpSize);
        }
    }

    [Fact]
    public void TryRecycle_LeavesAReparseDirectoryInPlace()
    {
        string target = Path.Combine(_root, "target");
        string link = Path.Combine(_root, "link");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "stay");
        Assert.True(CreateJunction(link, target), "mklink /J başarısız — koruma test edilemiyor");

        RecycleBinNative.RecycleCallResult result = RecycleBinNative.TryRecycle([link], IntPtr.Zero);
        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Rejected);
        Assert.False(result.ApiFailed);
        Assert.True(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void TryRecycle_DoesNotClaimSuccessForAMissingFile()
    {
        string missing = Path.Combine(_root, "missing.txt");
        RecycleBinNative.RecycleCallResult result = RecycleBinNative.TryRecycle([missing], IntPtr.Zero);
        Assert.Equal(0, result.Accepted);
        Assert.False(result.ApiFailed);
        Assert.False(File.Exists(missing));
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
