using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>Oturum güç eylemleri (üst çubuk logo menüsü): kilitle, uyku, kapat, yeniden başlat.</summary>
internal static class PowerActions
{
    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    internal static bool TryLock()
    {
        bool ok = LockWorkStation();
        if (!ok)
        {
            Debug.WriteLine("WharfBar: LockWorkStation failed");
        }

        return ok;
    }

    internal static bool TrySleep()
    {
        bool ok = SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
        if (!ok)
        {
            Debug.WriteLine("WharfBar: SetSuspendState failed");
        }

        return ok;
    }

    internal static bool TryShutdown() => RunShutdown("/s");

    internal static bool TryRestart() => RunShutdown("/r");

    private static bool RunShutdown(string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", $"{args} /t 0")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: shutdown failed — {ex.Message}");
            return false;
        }
    }
}
