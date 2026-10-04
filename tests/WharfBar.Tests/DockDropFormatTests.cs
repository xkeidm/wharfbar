using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockDropFormatTests
{
    [Theory]
    [InlineData(@"C:\apps\thing.exe", true)]
    [InlineData(@"C:\apps\thing.EXE", true)]
    [InlineData(@"C:\apps\thing.lnk", true)]
    [InlineData(@"C:\apps\thing.bat", true)]
    [InlineData(@"C:\apps\thing.cmd", true)]
    [InlineData(@"C:\apps\notes.txt", false)]
    [InlineData(@"C:\apps\folder", false)]
    [InlineData("", false)]
    public void IsSupported_FollowsTheExtensionAllowList(string path, bool expected)
    {
        Assert.Equal(expected, DockDropFormats.IsSupported(path));
    }

    [Fact]
    public void Resolve_WhenExtensionIsUnsupported_ReturnsEmpty()
    {
        (string path, string args) = DockDropFormats.Resolve(@"C:\apps\notes.txt");
        Assert.Equal(string.Empty, path);
        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void Resolve_WhenPlainExecutable_ReturnsPathUnchanged()
    {
        (string path, string args) = DockDropFormats.Resolve(@"C:\apps\thing.exe");
        Assert.Equal(@"C:\apps\thing.exe", path);
        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void Resolve_WhenShortcutIsUnreadable_KeepsShortcutPath()
    {
        // No such file on disk: the resolver cannot read it, so the .lnk path is used and
        // the store derives a label from the file name.
        (string path, _) = DockDropFormats.Resolve(@"C:\nope\missing.lnk");
        Assert.Equal(@"C:\nope\missing.lnk", path);
    }

    [Fact]
    public void Resolve_WhenShortcutIsReadable_ResolvesTargetAndArgs()
    {
        // The happy path is what makes a dropped shortcut launch the target instead of the
        // .lnk, so it is covered against a real shortcut file — the fallback test above
        // would stay green even if the whole TryResolve branch were deleted.
        string target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "notepad.exe");
        string shortcut = Path.Combine(Path.GetTempPath(), $"wharfbar-{Guid.NewGuid():N}.lnk");
        CreateShortcut(shortcut, target, "--dock-test");

        try
        {
            (string path, string args) = DockDropFormats.Resolve(shortcut);

            Assert.Equal(target, path, ignoreCase: true);
            Assert.Equal("--dock-test", args);
        }
        finally
        {
            File.Delete(shortcut);
        }
    }

    /// <summary>
    /// Writes a real .lnk through the same COM object <c>ShortcutResolver</c> reads it with.
    /// Reflection rather than <c>dynamic</c> so the test exercises the production code path.
    /// </summary>
    private static void CreateShortcut(string path, string target, string args)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not registered");
        object shell = Activator.CreateInstance(shellType)!;

        try
        {
            object shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                [path])!;

            try
            {
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [target]);
                shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, [args]);
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}