using WharfBar.Native;

namespace WharfBar.Services;

internal sealed class TrayOverflowInvoker
{
    internal bool TryOpen()
    {
        try
        {
            if (!SendInputKeys.TrySendWinB())
            {
                System.Diagnostics.Debug.WriteLine("WharfBar: Win+B SendInput failed");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WharfBar: tray overflow invoke failed — {ex.Message}");
            return false;
        }
    }
}
