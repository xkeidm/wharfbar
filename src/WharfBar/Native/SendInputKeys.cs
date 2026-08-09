using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class SendInputKeys
{
    internal static bool TrySendWinB()
    {
        var inputs = new INPUT[]
        {
            CreateKeyInput(NativeMethods.VK_LWIN, keyUp: false),
            CreateKeyInput(NativeMethods.VK_B, keyUp: false),
            CreateKeyInput(NativeMethods.VK_B, keyUp: true),
            CreateKeyInput(NativeMethods.VK_LWIN, keyUp: true),
        };

        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == 4;
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
