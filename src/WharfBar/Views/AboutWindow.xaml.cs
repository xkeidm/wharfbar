using System.Windows;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>Simple About dialog — not an AppBar; no shell registration.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = AppVersion.IsPrerelease
            ? $"Sürüm {AppVersion.Current} · Beta"
            : $"Sürüm {AppVersion.Current}";
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();
}
