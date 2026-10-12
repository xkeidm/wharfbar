namespace WharfBar.Services;

/// <summary>
/// Karo ışığı, yazma isteğinin kendisinden değil okunan durumdan gelir.
/// Anahtar değeri yalnız Ayarlar anahtarının geri okumasıdır; yazma başarısızsa null kalır.
/// </summary>
internal static class EnergySaverTile
{
    internal static bool IsActive(bool apiOn, bool systemFlagOn, bool? alwaysOnSwitch)
        => apiOn || systemFlagOn || alwaysOnSwitch == true;

    internal static bool RequestedOn(bool apiOn, bool systemFlagOn, bool? alwaysOnSwitch)
        => !IsActive(apiOn, systemFlagOn, alwaysOnSwitch);
}
