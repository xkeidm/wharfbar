using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Snapshot of AC / battery state from <c>GetSystemPowerStatus</c>.
/// Desktop PCs without a battery report <see cref="IsBatteryPresent"/> as false.
/// </summary>
internal readonly struct PowerStatusInfo
{
    internal bool IsAcOnline { get; init; }
    internal bool IsBatteryPresent { get; init; }
    internal int Percent { get; init; }
    internal bool IsCharging { get; init; }
}

/// <summary>Thin wrapper around <c>kernel32!GetSystemPowerStatus</c>.</summary>
internal static class PowerStatus
{
    private const byte AcOnline = 1;
    private const byte BatteryFlagNoBattery = 128;
    private const byte BatteryFlagCharging = 8;
    private const byte PercentUnknown = 255;

    internal static PowerStatusInfo Query()
    {
        if (!NativeMethods.GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            return default;
        }

        bool noBattery = (status.BatteryFlag & BatteryFlagNoBattery) != 0
            || status.BatteryLifePercent == PercentUnknown;

        int percent = noBattery ? 0 : Math.Clamp((int)status.BatteryLifePercent, 0, 100);

        return new PowerStatusInfo
        {
            IsAcOnline = status.ACLineStatus == AcOnline,
            IsBatteryPresent = !noBattery,
            Percent = percent,
            IsCharging = !noBattery && (status.BatteryFlag & BatteryFlagCharging) != 0,
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}
