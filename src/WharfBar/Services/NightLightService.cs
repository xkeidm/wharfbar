using System.Diagnostics;
using Microsoft.Win32;

namespace WharfBar.Services;

/// <summary>
/// Best-effort gece ışığı okuma/yazma. Registry blob tanınmazsa yazılmaz ve karo değişmez;
/// daha önce yazılmış bir oturum override'ı, okuma tekrar tanıyana kadar durur.
/// </summary>
internal sealed class NightLightService
{
    private const string StateKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    private bool? _override;
    private bool _lastKnown;

    internal bool IsEnabled => _override ?? _lastKnown;

    /// <summary>
    /// Registry yazımı tutarsa karo yeni duruma geçer. Blob tanınmıyorsa override kurulmaz;
    /// çağıran Windows Ayarları'nı açabilir.
    /// </summary>
    internal bool SetEnabled(bool enabled)
    {
        if (!TryWriteRegistry(enabled))
        {
            return false;
        }

        _override = enabled;
        _lastKnown = enabled;
        return true;
    }

    /// <summary>Registry'yi yeniden okur; dışarıdan değişiklik okunabilir ve override'dan
    /// farklıysa override düşer (işletim sistemi kazanır).</summary>
    internal void PollRegistry()
    {
        byte[]? data = ReadData();
        if (!NightLightBlob.TryParse(data, out bool enabled))
        {
            return;
        }

        _lastKnown = enabled;
        if (_override is bool ov && ov != enabled)
        {
            _override = null;
        }
    }

    private static byte[]? ReadData()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StateKeyPath);
            return key?.GetValue("Data") as byte[];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Night light read failed — {ex.Message}");
            return null;
        }
    }

    private static bool TryWriteRegistry(bool enabled)
    {
        try
        {
            byte[]? data = ReadData();
            if (data is null || !NightLightBlob.TryParse(data, out bool current))
            {
                Debug.WriteLine("WharfBar: Night light write skipped — blob not recognised");
                return false;
            }

            if (current == enabled)
            {
                return true;
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(StateKeyPath);
            key.SetValue("Data", NightLightBlob.ToggleFlag(data, enabled), RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Night light write failed — {ex.Message}");
            return false;
        }
    }
}