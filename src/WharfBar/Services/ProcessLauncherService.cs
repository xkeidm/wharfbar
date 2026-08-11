using System.Diagnostics;

namespace WharfBar.Services;

internal sealed class ProcessLauncherService
{
    internal void Launch(string path, string args)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = args ?? string.Empty,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launch failed: {path} — {ex.Message}");
        }
    }

    internal void LaunchAsAdmin(string path, string args)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = args ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launch as admin failed: {path} — {ex.Message}");
        }
    }
}
