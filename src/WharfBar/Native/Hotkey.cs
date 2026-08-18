using System.Runtime.InteropServices;
using System.Text;

namespace WharfBar.Native;

/// <summary>Win32 global hotkey registration (<c>RegisterHotKey</c>).</summary>
internal static class HotkeyNative
{
    internal const int WM_HOTKEY = 0x0312;

    internal const uint MOD_ALT = 0x0001;

    internal const uint MOD_CONTROL = 0x0002;

    internal const uint MOD_SHIFT = 0x0004;

    internal const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>
/// Parses <c>Ctrl+F11</c>-style hotkey strings and formats back to canonical form.
/// Supported keys: F1–F12, A–Z, 0–9. At least one modifier (Ctrl/Alt/Shift/Win) required.
/// </summary>
internal static class HotkeyParser
{
    internal static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        uint mods = 0;
        uint key = 0;
        foreach (string part in parts)
        {
            if (TryParseModifier(part, out uint m))
            {
                if ((mods & m) != 0)
                {
                    return false; // tekrar eden modifier
                }

                mods |= m;
            }
            else if (key == 0 && TryParseKey(part, out uint k))
            {
                key = k;
            }
            else
            {
                return false;
            }
        }

        if (mods == 0 || key == 0)
        {
            return false;
        }

        modifiers = mods;
        vk = key;
        return true;
    }

    internal static string Format(uint modifiers, uint vk)
    {
        var sb = new StringBuilder();
        AppendModifier(sb, modifiers, HotkeyNative.MOD_CONTROL, "Ctrl");
        AppendModifier(sb, modifiers, HotkeyNative.MOD_ALT, "Alt");
        AppendModifier(sb, modifiers, HotkeyNative.MOD_SHIFT, "Shift");
        AppendModifier(sb, modifiers, HotkeyNative.MOD_WIN, "Win");
        AppendKey(sb, vk);
        return sb.ToString();
    }

    private static bool TryParseModifier(string text, out uint modifier)
    {
        modifier = text.ToLowerInvariant() switch
        {
            "ctrl" or "control" => HotkeyNative.MOD_CONTROL,
            "alt" => HotkeyNative.MOD_ALT,
            "shift" => HotkeyNative.MOD_SHIFT,
            "win" or "windows" => HotkeyNative.MOD_WIN,
            _ => 0,
        };
        return modifier != 0;
    }

    private static bool TryParseKey(string text, out uint vk)
    {
        vk = 0;
        if (text.Length == 1 && text[0] is >= 'A' and <= 'Z')
        {
            vk = (uint)text[0];
            return true;
        }

        if (text.Length == 1 && text[0] is >= '0' and <= '9')
        {
            vk = (uint)text[0];
            return true;
        }

        if (text.Length >= 2 && text[0] is 'F' or 'f')
        {
            if (int.TryParse(text.AsSpan(1), out int f) && f is >= 1 and <= 12)
            {
                vk = (uint)(0x70 + f - 1);
                return true;
            }
        }

        return false;
    }

    private static void AppendModifier(StringBuilder sb, uint modifiers, uint flag, string label)
    {
        if ((modifiers & flag) != 0)
        {
            if (sb.Length > 0)
            {
                sb.Append('+');
            }

            sb.Append(label);
        }
    }

    private static void AppendKey(StringBuilder sb, uint vk)
    {
        if (sb.Length > 0)
        {
            sb.Append('+');
        }

        if (vk is >= 0x70 and <= 0x7B)
        {
            sb.Append('F').Append(vk - 0x70 + 1);
        }
        else
        {
            sb.Append((char)vk);
        }
    }
}
