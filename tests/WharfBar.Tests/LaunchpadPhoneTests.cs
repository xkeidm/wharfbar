using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Services;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

public class LaunchpadPhoneTests
{
    [Fact]
    public void ParseCompanion_ReadsNameBatteryNoticesAndHidesBluetoothWhenOff()
    {
        const string json = """
            {
              "body": [
                { "type": "TextBlock", "text": "Pixel", "weight": "Bolder" },
                { "type": "Image", "altText": "Bilgisayarınıza bağlı", "tooltip": "Bilgisayarınıza bağlı" },
                { "type": "Image", "altText": "Bluetooth: Mobil cihazınız için kapalı", "tooltip": "Bluetooth: Mobil cihazınız için kapalı" },
                { "type": "TextBlock", "text": "%81" },
                { "type": "Action.OpenUrl", "url": "ms-phone:messages?ref=1" },
                { "type": "Action.OpenUrl", "url": "ms-phone:calling?ref=1" },
                { "type": "Action.OpenUrl", "url": "ms-phone:photos?deviceid=abc" },
                { "type": "Action.OpenUrl", "url": "ms-phone:filetransfer?ref=1" },
                { "type": "TextBlock", "text": "En son", "weight": "Bolder" },
                { "type": "ImageSet", "images": [
                    { "type": "Image", "url": "data:image/png;base64,BBBB" },
                    { "type": "Image", "url": "data:image/png;base64,CCCC" }
                ] },
                {
                  "type": "ColumnSet",
                  "items": [
                    { "type": "Image", "url": "data:image/png;base64,AAAA" },
                    { "type": "TextBlock", "text": "Gmail" },
                    { "type": "TextBlock", "text": "19 yeni bildirim" }
                  ]
                },
                {
                  "type": "ColumnSet",
                  "items": [
                    { "type": "TextBlock", "text": "getir" },
                    { "type": "TextBlock", "text": "1 yeni bildirim" }
                  ]
                }
              ]
            }
            """;

        LaunchpadPhoneSnapshot? phone = LaunchpadPhoneLink.ParseCompanion(json);

        Assert.NotNull(phone);
        Assert.Equal("Pixel", phone.Name);
        Assert.Equal(81, phone.BatteryPercent);
        Assert.Equal("%81", LaunchpadPhoneLink.FormatBattery(phone.BatteryPercent));
        Assert.False(phone.BluetoothOn);
        Assert.True(phone.Connected);
        Assert.True(phone.SendFiles);
        Assert.Equal(["Gmail", "getir"], phone.Recent.Select(notice => notice.AppName).ToArray());
        Assert.Equal("19 yeni bildirim", phone.Recent[0].Summary);
        Assert.Equal("data:image/png;base64,AAAA", phone.Recent[0].IconDataUri);
        Assert.Equal("data:image/png;base64,BBBB", phone.DeviceImageUri);
        Assert.Equal("data:image/png;base64,CCCC", phone.DeviceFrameUri);
    }

    [Fact]
    public void ParseCompanion_LeavesBatteryHiddenWhenTheCardHasNone()
    {
        const string json = """
            { "body": [ { "type": "TextBlock", "text": "Pixel", "weight": "Bolder" } ] }
            """;

        LaunchpadPhoneSnapshot? phone = LaunchpadPhoneLink.ParseCompanion(json);

        Assert.NotNull(phone);
        Assert.Null(phone.BatteryPercent);
        Assert.Null(LaunchpadPhoneLink.FormatBattery(phone.BatteryPercent));
        Assert.Null(phone.BluetoothOn);
        Assert.False(phone.SendFiles);
        Assert.Empty(phone.Recent);
    }

    [Fact]
    public void Compose_UsesMetadataNameWithoutInventingBattery()
    {
        const string metadata = """
            {
              "DeviceMetadatas": {
                "account": [
                  {
                    "IsLinked": true,
                    "Metadata": {
                      "LastSeenTime": "2026-10-12T10:00:00",
                      "Metadata": {
                        "ClientType": "WEA",
                        "OsName": "Windows",
                        "DisplayName": "Bu bilgisayar",
                        "IsEnabled": true
                      }
                    }
                  },
                  {
                    "IsLinked": true,
                    "Metadata": {
                      "LastSeenTime": "2026-10-12T12:00:00",
                      "Metadata": {
                        "ClientType": "LTW",
                        "OsName": "Android",
                        "DisplayName": "Pixel",
                        "IsEnabled": true
                      }
                    }
                  }
                ]
              }
            }
            """;

        LaunchpadPhoneSnapshot? phone = LaunchpadPhoneLink.Compose(null, metadata, "Kayıt");

        Assert.NotNull(phone);
        Assert.Equal("Pixel", phone.Name);
        Assert.Null(phone.BatteryPercent);
        Assert.Empty(phone.Recent);
        Assert.Null(LaunchpadPhoneLink.ParseCompanion(" "));
        Assert.Null(LaunchpadPhoneLink.Compose(null, """{"DeviceMetadatas":{}}""", "  "));
    }

    [Fact]
    public void ParseCompanion_CapsNoticesAtFour()
    {
        string items = string.Join(
            ",",
            Enumerable.Range(1, 6).Select(index => $$"""
                { "type": "ColumnSet", "items": [
                    { "type": "TextBlock", "text": "Uygulama {{index}}" },
                    { "type": "TextBlock", "text": "{{index}} yeni bildirim" }
                ] }
                """));
        string json = $$"""
            { "body": [
                { "type": "TextBlock", "text": "Pixel", "weight": "Bolder" },
                {{items}}
            ] }
            """;

        LaunchpadPhoneSnapshot? phone = LaunchpadPhoneLink.ParseCompanion(json);

        Assert.NotNull(phone);
        Assert.Equal(4, phone.Recent.Count);
        Assert.Equal("Uygulama 1", phone.Recent[0].AppName);
        Assert.Equal("Uygulama 4", phone.Recent[3].AppName);
    }

    [Fact]
    public void Row_ShowsDeviceNameAndHidesBatteryWhenNull()
    {
        RunSta(() =>
        {
            string? opened = null;
            var phone = new LaunchpadPhoneSnapshot("Pixel", null, false, true, false, []);
            UIElement row = LaunchpadPhoneRow.Create(phone, path => opened = path);

            List<string> texts = Texts(row);
            Assert.Contains("Pixel", texts);
            Assert.Contains("Mesajlar", texts);
            Assert.Contains("Aramalar", texts);
            Assert.Contains("Fotoğraflar", texts);
            Assert.DoesNotContain("Dosyaları gönder", texts);
            Assert.DoesNotContain(texts, text => text.Contains('%'));
            Assert.DoesNotContain("En son", texts);

            Click(row, "Aramalar");
            Assert.Equal(LaunchpadPhoneLink.CallsUri, opened);
        });
    }

    [Fact]
    public void Row_ShowsBatteryAndFilesWhenKnown()
    {
        RunSta(() =>
        {
            string? opened = null;
            var phone = new LaunchpadPhoneSnapshot(
                "Pixel",
                81,
                true,
                true,
                true,
                [new LaunchpadPhoneNotice("Gmail", "19 yeni bildirim")]);
            UIElement row = LaunchpadPhoneRow.Create(phone, path => opened = path);

            List<string> texts = Texts(row);
            Assert.Contains("%81", texts);
            Assert.Contains("Gmail", texts);
            Assert.Contains("19 yeni bildirim", texts);
            Assert.Contains("En son", texts);
            Click(row, "Dosyaları gönder");
            Assert.Equal(LaunchpadPhoneLink.FilesUri, opened);
            Assert.Equal(LaunchpadPhoneRow.PanelWidth, ((FrameworkElement)row).Width);
        });
    }

    [Fact]
    public void ScreenHole_FindsTheTransparentInterior()
    {
        RunSta(() =>
        {
            var frame = new WriteableBitmap(20, 36, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[20 * 36 * 4];
            for (int y = 0; y < 36; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    bool bezel = x < 3 || x >= 17 || y < 3 || y >= 33;
                    if (!bezel)
                    {
                        continue;
                    }

                    int offset = ((y * 20) + x) * 4;
                    pixels[offset + 3] = 255;
                }
            }

            frame.WritePixels(new Int32Rect(0, 0, 20, 36), pixels, 20 * 4, 0);
            Int32Rect? hole = LaunchpadPhoneRow.ScreenHole(frame);

            Assert.Equal(new Int32Rect(3, 3, 14, 30), hole);
        });
    }

    [Fact]
    public void Row_PlacesTheScreenshotInsideThePhone()
    {
        RunSta(() =>
        {
            string screen = Png(Solid(14, 40, 200, 20, 20));
            string frame = Png(Bezel(20, 36, 3));
            var phone = new LaunchpadPhoneSnapshot(
                "Pixel",
                null,
                null,
                true,
                false,
                [],
                screen,
                frame);
            UIElement row = LaunchpadPhoneRow.Create(phone, _ => { });

            Image[] images = Walk(row).OfType<Image>().ToArray();
            Assert.Equal(2, images.Length);
            Assert.Equal(LaunchpadPhoneRow.PhoneMarkWidth, images[1].Width);
            Assert.True(images[1].Height > images[1].Width);
            Canvas layer = Assert.IsType<Canvas>(images[0].Parent);
            Assert.True(layer.Clip is RectangleGeometry clip
                && clip.Rect.Width < LaunchpadPhoneRow.PhoneMarkWidth
                && clip.Rect.Height > clip.Rect.Width);
            Assert.True(Canvas.GetLeft(images[0]) > 0);
            Assert.True(Canvas.GetTop(images[0]) > 0);
        });
    }

    [Fact]
    public void Beside_PlacesThePanelToTheRightOfTheCard()
    {
        RunSta(() =>
        {
            var card = new TextBlock { Text = "Kutu" };
            var phone = new LaunchpadPhoneSnapshot("Pixel", null, null, true, false, []);
            UIElement stage = LaunchpadPhoneRow.Beside(card, phone, _ => { });

            List<string> texts = Texts(stage);
            Assert.Contains("Kutu", texts);
            Assert.Contains("Pixel", texts);
            Assert.Contains("Mesajlar", texts);
            Assert.Contains(Walk(stage).OfType<FrameworkElement>(), element => element.Width == LaunchpadPhoneRow.PanelWidth);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null)
        {
            throw error;
        }
    }

    private static void Click(DependencyObject root, string content)
    {
        Button? button = FindButtons(root).FirstOrDefault(candidate => Texts(candidate).Contains(content));
        Assert.NotNull(button);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static List<string> Texts(DependencyObject root)
    {
        var texts = new List<string>();
        foreach (DependencyObject node in Walk(root))
        {
            if (node is TextBlock block && block.Text.Length > 0)
            {
                texts.Add(block.Text);
            }

            if (node is ContentControl control && control.Content is string label && label.Length > 0)
            {
                texts.Add(label);
            }
        }

        return texts;
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        foreach (DependencyObject node in Walk(root))
        {
            if (node is Button button)
            {
                yield return button;
            }
        }
    }

    private static WriteableBitmap Solid(int width, int height, byte red, byte green, byte blue)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[width * height * 4];
        for (int index = 0; index < width * height; index++)
        {
            pixels[index * 4] = blue;
            pixels[(index * 4) + 1] = green;
            pixels[(index * 4) + 2] = red;
            pixels[(index * 4) + 3] = 255;
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return bitmap;
    }

    private static WriteableBitmap Bezel(int width, int height, int thickness)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool edge = x < thickness || x >= width - thickness || y < thickness || y >= height - thickness;
                if (!edge)
                {
                    continue;
                }

                int offset = ((y * width) + x) * 4;
                pixels[offset + 3] = 255;
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return bitmap;
    }

    private static string Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject node)
    {
        yield return node;
        foreach (object child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is not DependencyObject next)
            {
                continue;
            }

            foreach (DependencyObject descendant in Walk(next))
            {
                yield return descendant;
            }
        }
    }
}
