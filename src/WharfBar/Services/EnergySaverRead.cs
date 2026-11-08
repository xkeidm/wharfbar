using System.Runtime.InteropServices;

namespace WharfBar.Services;

/// <summary>
/// Documented reads of energy saver state. No writes here.
/// </summary>
internal static class EnergySaverRead
{
    internal static bool ApiOn()
    {
        try
        {
            // EnergySaverStatus2 is not in this target's WinRT projection (19041).
            return Windows.System.Power.PowerManager.EnergySaverStatus
                == Windows.System.Power.EnergySaverStatus.On;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WharfBar: energy saver read failed — {ex.Message}");
            return false;
        }
    }

    /// <summary><c>SYSTEM_POWER_STATUS.SystemStatusFlag</c> of 1 means energy saver is on.</summary>
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
