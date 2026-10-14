using System.Windows.Controls;
using WharfBar.Resources;
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
            ? string.Format(Strings.About_VersionBetaFormat, AppVersion.Current)
            : string.Format(Strings.About_VersionFormat, AppVersion.Current);

        string os = SystemVersion.IsWindows11 ? "Windows 11" : "Windows 10";
        PlatformText.Text = $".NET {Environment.Version.Major} — {os}";
    }
}
