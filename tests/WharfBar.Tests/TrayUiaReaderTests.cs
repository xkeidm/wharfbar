using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayUiaReaderTests
{
    [Theory]
    [InlineData("SystemTray.NormalButton", "Monitorian", true)]
    [InlineData("SystemTray.NormalButton", "EarTrumpet: 83% - Kulaklık İşitme Bölümü (USB)", true)]
    [InlineData("SystemTray.NormalButton", "Windows Güvenliği - Eylem gerekmiyor.", true)]
    [InlineData("SystemTray.NormalButton", "G HUB Logitech G HUB", true)]
    [InlineData("SystemTray.NormalButton", "Tepsi Girişi Göstergesi", false)]
    [InlineData("SystemTray.NormalButton", "WharfBar", false)]
    [InlineData("SystemTray.AccentButton", "Ağ Ethernet\nİnternet erişimi", false)]
    [InlineData("SystemTray.OmniButtonLeft", "Saat 20:02", false)]
    [InlineData("SystemTray.OmniButtonRight", "Birim Kulaklık İşitme Bölümü: 83%", false)]
    [InlineData("SystemTray.ShowDesktopButton", "Masaüstünü Göster", false)]
    [InlineData("Taskbar.TaskListButtonAutomationPeer", "Dosya Gezgini - 1 çalışan pencere", false)]
    [InlineData("ToggleButton", "Başlat", false)]
    public void IsAppIcon_FiltersByClassAndName(string className, string name, bool expected) =>
        Assert.Equal(expected, TrayUiaReader.IsAppIcon(className, name));
}
