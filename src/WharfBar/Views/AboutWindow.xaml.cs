using System.Reflection;
using System.Windows;

namespace WharfBar.Views;

/// <summary>Simple About dialog — not an AppBar; no shell registration.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Sürüm {ResolveVersion()}";
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();

    private static string ResolveVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip optional "+gitsha" suffix from informational versions.
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        Version? v = assembly.GetName().Version;
        return v is null ? "—" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
