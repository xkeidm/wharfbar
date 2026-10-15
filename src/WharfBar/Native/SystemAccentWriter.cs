using System.Runtime.InteropServices;
using Microsoft.Win32;
using WharfBar.Services;

namespace WharfBar.Native;

/// <summary>
/// Best-effort Windows Personalization accent write (undocumented HKCU keys).
/// Failure is silent — callers must not depend on success.
/// </summary>
internal static class SystemAccentWriter
{
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    internal static bool TryApply(IslandRgb color)
    {
        try
        {
            uint dword = SystemAccentPack.ToAccentDword(color);
            byte[] palette = SystemAccentPack.BuildAccentPalette(color);

            using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(AccentKey, writable: true))
            {
                if (key is null)
                {
                    return false;
                }

                key.SetValue("AccentColor", unchecked((int)dword), RegistryValueKind.DWord);
                key.SetValue("AccentColorMenu", unchecked((int)dword), RegistryValueKind.DWord);
                key.SetValue("StartColorMenu", unchecked((int)dword), RegistryValueKind.DWord);
                key.SetValue("AccentPalette", palette, RegistryValueKind.Binary);
            }

            using (RegistryKey? dwm = Registry.CurrentUser.CreateSubKey(DwmKey, writable: true))
            {
                dwm?.SetValue("AccentColor", unchecked((int)dword), RegistryValueKind.DWord);
                dwm?.SetValue("ColorizationColor", unchecked((int)dword), RegistryValueKind.DWord);
            }

            BroadcastImmersiveColorSet();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void BroadcastImmersiveColorSet()
    {
        IntPtr lParam = Marshal.StringToHGlobalUni("ImmersiveColorSet");
        try
        {
            _ = NativeMethods.SendMessageTimeout(
                NativeMethods.HWND_BROADCAST,
                (uint)NativeMethods.WM_SETTINGCHANGE,
                IntPtr.Zero,
                lParam,
                NativeMethods.SMTO_ABORTIFHUNG,
                1000,
                out _);
        }
        finally
        {
            Marshal.FreeHGlobal(lParam);
        }
    }
}
