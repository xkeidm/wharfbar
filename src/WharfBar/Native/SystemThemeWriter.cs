using Microsoft.Win32;

namespace WharfBar.Native;

/// <summary>
/// Best-effort Windows Personalization light/dark write (HKCU Themes\Personalize).
/// Failure is silent — callers must not depend on success. No exit restore.
/// </summary>
internal static class SystemThemeWriter
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <param name="light">true = light mode (DWORD 1), false = dark (DWORD 0).</param>
    internal static bool TryApply(bool light)
    {
        try
        {
            int dword = light ? 1 : 0;
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue("AppsUseLightTheme", dword, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", dword, RegistryValueKind.DWord);
            ImmersiveColorNotify.Broadcast();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
