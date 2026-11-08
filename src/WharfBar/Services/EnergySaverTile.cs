namespace WharfBar.Services;

/// <summary>
/// The tile's light comes from the read state, not from the write request itself.
/// The switch value is only the read-back of the Settings switch; if the write fails it stays null.
/// </summary>
internal static class EnergySaverTile
{
    internal static bool IsActive(bool apiOn, bool systemFlagOn, bool? alwaysOnSwitch)
        => apiOn || systemFlagOn || alwaysOnSwitch == true;

    internal static bool RequestedOn(bool apiOn, bool systemFlagOn, bool? alwaysOnSwitch)
        => !IsActive(apiOn, systemFlagOn, alwaysOnSwitch);
}
