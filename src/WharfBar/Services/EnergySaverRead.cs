using System.Runtime.InteropServices;

namespace WharfBar.Services;

/// <summary>
/// Enerji tasarrufunun belgelenmiş okumaları. Yazma burada yok.
/// </summary>
internal static class EnergySaverRead
{
    internal static bool ApiOn()
    {
        try
        {
            // EnergySaverStatus2 bu hedefin WinRT projeksiyonunda yok (19041).
            return Windows.System.Power.PowerManager.EnergySaverStatus
                == Windows.System.Power.EnergySaverStatus.On;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WharfBar: energy saver read failed — {ex.Message}");
            return false;
        }
    }

    /// <summary><c>SYSTEM_POWER_STATUS.SystemStatusFlag</c> 1 ise tasarruf açıktır.</summary>
    internal static bool SystemFlagOn()
    {
        try
        {
            return GetSystemPowerStatus(out SystemPowerStatus status) && status.SystemStatusFlag == 1;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WharfBar: power status read failed — {ex.Message}");
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
