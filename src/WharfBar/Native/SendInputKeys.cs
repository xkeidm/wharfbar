using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class SendInputKeys
{
    internal static bool TrySendWinB() => TrySendChord(NativeMethods.VK_LWIN, NativeMethods.VK_B);

    /// <summary>Opens the Windows 10/11 Notification Center / calendar pane focus path.</summary>
    internal static bool TrySendWinN() => TrySendChord(NativeMethods.VK_LWIN, NativeMethods.VK_N);

    /// <summary>Opens the Project / display projection UI (Win+P).</summary>
    internal static bool TrySendWinP() => TrySendChord(NativeMethods.VK_LWIN, NativeMethods.VK_P);

    internal static bool TrySendReturn() => TrySendKey(NativeMethods.VK_RETURN);

    internal static bool TrySendSpace() => TrySendKey(NativeMethods.VK_SPACE);

    private static bool TrySendChord(ushort modifier, ushort key)
    {
        var inputs = new INPUT[]
        {
            CreateKeyInput(modifier, keyUp: false),
            CreateKeyInput(key, keyUp: false),
            CreateKeyInput(key, keyUp: true),
            CreateKeyInput(modifier, keyUp: true),
        };

        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == (uint)inputs.Length;
    }

    private static bool TrySendKey(ushort virtualKey)
    {
        var inputs = new INPUT[]
        {
            CreateKeyInput(virtualKey, keyUp: false),
            CreateKeyInput(virtualKey, keyUp: true),
        };

        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == (uint)inputs.Length;
    }

    private static INPUT CreateKeyInput(ushort virtualKey, bool keyUp)
    {
        return new INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }
}
