using System.Reflection;
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

        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "—";

        VersionText.Text = $"Sürüm {version}";

        string os = SystemVersion.IsWindows11 ? "Windows 11" : "Windows 10";
        PlatformText.Text = $".NET {Environment.Version.Major} — {os}";
    }
}
