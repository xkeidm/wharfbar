using System.Windows.Controls;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views.Onboarding;

/// <summary>
/// First-run Welcome step: brand hero, version, the three things WharfBar adds and the taskbar
/// note. Informational only — it never reads or writes the configuration.
/// </summary>
public partial class WelcomeStep : UserControl
{
    public WelcomeStep()
    {
        InitializeComponent();

        // Appearance_Logo_* exists in the resx but not in the generated Designer (the Appearance
        // page reads it through LocalizationService too), so do the same here.
        WordmarkText.Text = LocalizationService.Get("Appearance_Logo_WharfBar");

        VersionText.Text = AppVersion.IsPrerelease
            ? string.Format(Strings.About_VersionBetaFormat, AppVersion.Current)
            : string.Format(Strings.About_VersionFormat, AppVersion.Current);
    }
}
