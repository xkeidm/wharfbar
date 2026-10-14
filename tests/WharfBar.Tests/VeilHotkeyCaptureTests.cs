using System.Windows.Input;
using WharfBar.Native;
using WharfBar.Views.Pages;
using Xunit;

namespace WharfBar.Tests;

public class VeilHotkeyCaptureTests
{
    [Fact]
    public void PhysicalKey_UnwrapsAltChords()
    {
        Assert.Equal(Key.A, VeilPage.PhysicalKey(Key.System, Key.A));
        Assert.Equal(Key.F11, VeilPage.PhysicalKey(Key.System, Key.F11));
        Assert.Equal(Key.F11, VeilPage.PhysicalKey(Key.F11, Key.None));
    }

    [Fact]
    public void AltChord_FormatsAsARegisterableHotkey()
    {
        Key key = VeilPage.PhysicalKey(Key.System, Key.A);
        Assert.Equal(Key.A, key);
        uint vk = (uint)(key - Key.A + 0x41);
        string hotkey = HotkeyParser.Format(HotkeyNative.MOD_ALT, vk);
        Assert.Equal("Alt+A", hotkey);
        Assert.True(HotkeyParser.TryParse(hotkey, out uint modifiers, out uint parsed));
        Assert.Equal(HotkeyNative.MOD_ALT, modifiers);
        Assert.Equal(vk, parsed);
    }
}
