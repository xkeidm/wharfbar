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

        if (!TryCreateShortcut(shortcut, target, "--dock-test"))
        {
            // Not applicable here, not a pass: the shortcut could not be written, so there is
            // nothing to resolve. WSH is an optional, deprecated Windows component that hardening
            // policies routinely disable, and xUnit 2.9 has no runtime skip, so failing here would
            // make the whole suite depend on the machine rather than on the code. Production
            // degrades to the .lnk path (see Resolve_WhenShortcutIsUnreadable), so nothing is
            // under test in this state.
            return;
        }

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
    /// Returns false when the Windows Script Host is unavailable (not registered, or registered
    /// but refusing activation) so the caller can report the case as not applicable.
    /// </summary>
    private static bool TryCreateShortcut(string path, string target, string args)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return false;
        }

        object shell;
        try
        {
            shell = Activator.CreateInstance(shellType)!;
        }
        catch (Exception ex) when (ex is COMException or TypeLoadException or FileNotFoundException)
        {
            return false;
        }

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
        catch (COMException)
        {
            // Registered but unusable — e.g. the component is disabled by policy. Same verdict as
            // "not registered": this test cannot run, so it must not fail the suite.
            return false;
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return File.Exists(path);
    }
}
