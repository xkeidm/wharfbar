using System.Windows;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>Simple About dialog — not an AppBar; no shell registration.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = AppVersion.IsPrerelease
            ? string.Format(Strings.About_VersionBetaFormat, AppVersion.Current)
            : string.Format(Strings.About_VersionFormat, AppVersion.Current);
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();
}
