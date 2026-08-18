using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+F11", HotkeyNative.MOD_CONTROL, 0x7A)]
    [InlineData("ctrl+f11", HotkeyNative.MOD_CONTROL, 0x7A)]
    [InlineData("ctrl+a", HotkeyNative.MOD_CONTROL, 0x41)]
    [InlineData("Alt+Shift+A", HotkeyNative.MOD_ALT | HotkeyNative.MOD_SHIFT, 0x41)]
    [InlineData("Win+1", HotkeyNative.MOD_WIN, 0x31)]
    [InlineData("Ctrl+Alt+Shift+Win+F5", HotkeyNative.MOD_CONTROL | HotkeyNative.MOD_ALT | HotkeyNative.MOD_SHIFT | HotkeyNative.MOD_WIN, 0x74)]
    public void TryParse_ValidHotkey_Parses(string text, uint expectedMods, uint expectedVk)
    {
        Assert.True(HotkeyParser.TryParse(text, out uint modifiers, out uint vk));
        Assert.Equal(expectedMods, modifiers);
        Assert.Equal(expectedVk, vk);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("F11")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Space")]
    [InlineData("Ctrl+A+B")]
    public void TryParse_Invalid_ReturnsFalse(string? text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData(HotkeyNative.MOD_CONTROL, 0x7A, "Ctrl+F11")]
    [InlineData(HotkeyNative.MOD_CONTROL | HotkeyNative.MOD_ALT | HotkeyNative.MOD_SHIFT | HotkeyNative.MOD_WIN, 0x74, "Ctrl+Alt+Shift+Win+F5")]
    [InlineData(HotkeyNative.MOD_ALT | HotkeyNative.MOD_SHIFT, 0x41, "Alt+Shift+A")]
    [InlineData(HotkeyNative.MOD_WIN, 0x31, "Win+1")]
    public void Format_BuildsCanonicalString(uint modifiers, uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyParser.Format(modifiers, vk));
    }
}
