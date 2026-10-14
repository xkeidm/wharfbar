using System.Windows.Controls;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// About page: application info, version, platform.
/// </summary>
public partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();

        VersionText.Text = AppVersion.IsPrerelease
            ? $"Sürüm {AppVersion.Current} · Beta"
            : $"Sürüm {AppVersion.Current}";

        string os = SystemVersion.IsWindows11 ? "Windows 11" : "Windows 10";
        PlatformText.Text = $".NET {Environment.Version.Major} — {os}";
    }
}
