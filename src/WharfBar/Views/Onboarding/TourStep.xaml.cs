using System.Windows;
using System.Windows.Controls;
using WharfBar.Services;

namespace WharfBar.Views.Onboarding;

/// <summary>
/// Setup step: a static "how to use WharfBar" page. The mini top bar + dock at the top is pure
/// XAML decoration (<c>IsHitTestVisible=False</c>); the four cards explain the real controls.
/// Nothing here changes the config.
/// </summary>
public partial class TourStep : UserControl
{
    public TourStep()
    {
        InitializeComponent();

        // Show the mark the user picked in the appearance step (WharfBar mark by default).
        string? logoStyle = (Application.Current as App)?.Config?.Current.LogoStyle;
        MockBrandGlyph.Data = BrandLogo.BarGlyph(logoStyle);
    }
}
