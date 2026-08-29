using System.Linq;
using System.Windows;
using WharfBar.Services;

namespace WharfBar;

/// <summary>
/// Custom entry point: watchdog mode runs headless (no WPF app, no windows) so a
/// crashed WharfBar still gets its taskbar restore; normal mode boots the WPF app.
/// </summary>
internal static class Program
{
    [STAThread]
    internal static void Main(string[] args)
    {
        if (args.Any(static a => string.Equals(a, "--taskbar-watchdog", StringComparison.Ordinal)))
        {
            TaskbarWatchdog.Run();
            return;
        }

        App app = new();
        app.InitializeComponent();
        app.Run();
    }
}
