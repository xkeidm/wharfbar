using System.Windows;
using System.Windows.Media;

namespace WharfBar.Services;

/// <summary>
/// Flyout yüzeyi: tema arka plan renginin yarı saydam versiyonu (dock'un Acrylic yüzeyi gibi).
/// Arkadaki içerik hafifçe seçilir; her açılışta yeniden üretilir, böylece tema değişimi takip edilir.
/// </summary>
internal static class FlyoutSurface
{
    /// <summary>Yüzey opaklığı (~%94) — kalan ~%6 arkadaki içeriğin hafifçe görünmesini sağlar.</summary>
    private const byte Alpha = 0xF0;

    internal static SolidColorBrush Create()
    {
        Color color = (Application.Current.TryFindResource("ApplicationBackgroundBrush") as SolidColorBrush)?.Color
            ?? Color.FromRgb(0x20, 0x20, 0x20);
        color.A = Alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
