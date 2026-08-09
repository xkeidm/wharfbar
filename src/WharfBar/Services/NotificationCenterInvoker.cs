using System.Diagnostics;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Opens the native Windows Notification Center (Win+N). Does not mirror notification content
/// inside WharfBar (ADR-005 / boundaries).
/// </summary>
internal sealed class NotificationCenterInvoker
{
    internal bool TryOpen()
    {
        try
        {
            _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            if (!SendInputKeys.TrySendWinN())
            {
                Debug.WriteLine("WharfBar: Win+N SendInput failed");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: notification center invoke failed — {ex.Message}");
            return false;
        }
    }
}
