using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The island panel tint belongs to the SMTC session, not to the hub tab: while a session exists
/// the panel (and the joined pill that mirrors its top surface) keeps the album color on every
/// body — Clipboard, Wallpaper, Shelf, Prompter, Color — so both monitors show the same tint no
/// matter which hub tab they had saved. These tests drive the real
/// <see cref="IslandFlyout.PresentHub"/> with a synthetic <see cref="MediaSessionService"/> snapshot
/// (no SMTC session is needed).
/// </summary>
public class IslandPanelTintTests
{
    private static readonly Color Red = Color.FromRgb(0xDC, 0x28, 0x28);

    /// <summary>
    /// Every hub body that can be presented without a device host. Camera reads the microphone and
    /// starts live tracking (P/Invoke), so it is covered by the offscreen probe instead; its tint
    /// call site is the same <c>ApplySessionTint()</c> line as the tabs below.
    /// </summary>
    private static readonly IslandHubTab[] TintTabs =
    [
        IslandHubTab.Clipboard,
        IslandHubTab.Wallpaper,
        IslandHubTab.Shelf,
        IslandHubTab.Prompter,
        IslandHubTab.ColorPicker,
        IslandHubTab.Music,
    ];

    [Fact]
    public void PanelTint_TracksTheSession_OnEveryHubTab()
    {
        RunSta(() =>
        {
            IslandFlyout flyout = NewFlyout(redCover: true);
            flyout.ColorMode = IslandConfig.ColorModeAlbum;

            foreach (IslandHubTab tab in TintTabs)
            {
                flyout.PresentHub(tab, focus: false, crossfade: false);

                Assert.Equal(Red, flyout.SurfaceTint);
                // The joined pill's rim follows the same tint (alpha 0x55 = tinted top rim).
                Assert.Equal(0x55, flyout.JoinRimColor.A);
                Assert.Equal(Red.R, flyout.JoinRimColor.R);
                Assert.Equal(Red.G, flyout.JoinRimColor.G);
                Assert.Equal(Red.B, flyout.JoinRimColor.B);
            }

            flyout.DetachMedia();
        });
    }

    [Fact]
    public void PanelTint_IsNull_WhenNoSessionExists()
    {
        RunSta(() =>
        {
            IslandFlyout flyout = NewFlyout(redCover: false);
            flyout.ColorMode = IslandConfig.ColorModeAlbum;

            foreach (IslandHubTab tab in TintTabs)
            {
                flyout.PresentHub(tab, focus: false, crossfade: false);

                Assert.Null(flyout.SurfaceTint);
                // Neutral rim color (no tint applied).
                Assert.Equal(0x30, flyout.JoinRimColor.A);
            }

            flyout.DetachMedia();
        });
    }

    [Fact]
    public void ColorMode_RetintsWhileAClipboardBodyIsShown()
    {
        RunSta(() =>
        {
            IslandFlyout flyout = NewFlyout(redCover: true);
            flyout.ColorMode = IslandConfig.ColorModeAlbum;
            flyout.PresentHub(IslandHubTab.Clipboard, focus: false, crossfade: false);
            Assert.Equal(Red, flyout.SurfaceTint);

            // The old setter returned early while a non-Music body was shown; neutral must clear.
            flyout.ColorMode = IslandConfig.ColorModeNeutral;
            Assert.Null(flyout.SurfaceTint);

            flyout.ColorMode = IslandConfig.ColorModeAlbum;
            Assert.Equal(Red, flyout.SurfaceTint);
            flyout.DetachMedia();
        });
    }

    [Fact]
    public void PanelTint_FollowsSessionEnd_WhileANonMusicBodyIsShown()
    {
        RunSta(() =>
        {
            MediaSessionService media = Media(redCover: true);
            IslandFlyout flyout = new();
            flyout.AttachMedia(media);
            flyout.ColorMode = IslandConfig.ColorModeAlbum;
            flyout.PresentHub(IslandHubTab.Clipboard, focus: false, crossfade: false);
            Assert.Equal(Red, flyout.SurfaceTint);

            // Session gone: the panel must neutralize even though the Music body never shows.
            SetSnapshot(media, MediaSnapshot.Idle);
            RaiseChanged(media);
            Assert.Null(flyout.SurfaceTint);

            flyout.DetachMedia();
            media.Dispose();
        });
    }

    private static IslandFlyout NewFlyout(bool redCover)
    {
        MediaSessionService media = Media(redCover);
        var flyout = new IslandFlyout();
        flyout.AttachMedia(media);
        return flyout;
    }

    private static MediaSessionService Media(bool redCover)
    {
        var service = new MediaSessionService();
        if (redCover)
        {
            SetSnapshot(service, new MediaSnapshot(
                "probe-session",
                "Probe App",
                "Probe Track",
                "Probe Artist",
                IsPlaying: true,
                RedBitmap(),
                Position: TimeSpan.FromSeconds(1),
                Duration: TimeSpan.FromSeconds(2)));
        }

        return service;
    }

    private static ImageSource RedBitmap()
    {
        var bitmap = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Pbgra32, null);
        var pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = Red.B;
            pixels[i + 1] = Red.G;
            pixels[i + 2] = Red.R;
            pixels[i + 3] = 0xFF;
        }

        bitmap.WritePixels(new Int32Rect(0, 0, 4, 4), pixels, 4 * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static void SetSnapshot(MediaSessionService service, MediaSnapshot snapshot)
        => typeof(MediaSessionService)
            .GetField("_snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, snapshot);

    private static void RaiseChanged(MediaSessionService service)
        => (typeof(MediaSessionService)
                .GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(service) as EventHandler)
            ?.Invoke(service, EventArgs.Empty);

    /// <summary>Runs the WPF work on an STA thread (the same pattern as IslandFlyoutMotionTests).</summary>
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
            finally
            {
                try
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
                catch (InvalidOperationException)
                {
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }
}
