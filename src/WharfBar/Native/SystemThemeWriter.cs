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

    /// <summary>
    /// Writes the personalization values and broadcasts on the thread pool.
    ///
    /// The broadcast is <c>SendMessageTimeout(HWND_BROADCAST, …)</c>: Windows delivers the message to
    /// every top-level window synchronously and waits for each one, so running it on the UI thread
    /// froze the shell for tens to hundreds of milliseconds on every theme switch (and re-entered our
    /// own window procedures before the switch had finished). The write itself is a couple of
    /// registry values, so the whole operation belongs off the UI thread.
    /// </summary>
    internal static Task<bool> TryApplyAsync(bool light) =>
        Task.Run(() =>
        {
            try
            {
                return TryApply(light);
            }
            catch (Exception)
            {
                return false;
            }
        });

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
