using System.Diagnostics;
using Microsoft.Win32;

namespace WharfBar.Services;

/// <summary>
/// Best-effort night light read/write. If the registry blob is not recognized it is not written and the tile does not change;
/// a previously written session override stays until a read recognizes it again.
/// </summary>
internal sealed class NightLightService
{
    private const string StateKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    private bool? _override;
    private bool _lastKnown;

    internal bool IsEnabled => _override ?? _lastKnown;

    /// <summary>
    /// If the registry write sticks, the tile switches to the new state. If the blob is not recognized no override is set;
    /// the caller may open Windows Settings.
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

    /// <summary>Re-reads the registry; if the external change is readable and differs from
    /// the override, the override is dropped (the operating system wins).</summary>
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