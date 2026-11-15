using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WharfBar.Native;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Mixer session rows: the mute state has to be visible on the icon itself (the row dims the
/// icon and draws a slash across it), and an application whose icon cannot be read keeps its
/// identity as a letter tile instead of looking like a volume control.
/// </summary>
public class QuickSettingsMixerRowTests
{
    [Fact]
    public void MutedSession_DimsTheIconAndShowsTheSlash()
    {
        RunSta(() =>
        {
            DockPanel row = BuildRow(Session("Notepad", 1234, muted: true));
            (FrameworkElement icon, FrameworkElement slash) = IconParts(row);

            Assert.Equal(0.45, icon.Opacity, 3);
            Assert.Equal(Visibility.Visible, slash.Visibility);
        });
    }

    [Fact]
    public void PlayingSession_KeepsTheIconBrightAndHidesTheSlash()
    {
        RunSta(() =>
        {
            DockPanel row = BuildRow(Session("Notepad", 1234, muted: false));
            (FrameworkElement icon, FrameworkElement slash) = IconParts(row);

            Assert.Equal(1.0, icon.Opacity, 3);
            Assert.Equal(Visibility.Collapsed, slash.Visibility);
        });
    }

    [Fact]
    public void AppWithoutIcon_GetsTheLetterTile()
    {
        RunSta(() =>
        {
            DockPanel row = BuildRow(Session("OpenCode", 4242, muted: false));
            (FrameworkElement icon, _) = IconParts(row);

            Image image = Assert.IsType<Image>(icon);
            Assert.NotNull(image.Source);
        });
    }

    [Fact]
    public void SystemSounds_KeepTheSpeakerGlyph()
    {
        RunSta(() =>
        {
            DockPanel row = BuildRow(Session("System sounds", pid: 0, muted: false));
            (FrameworkElement icon, _) = IconParts(row);

            TextBlock glyph = Assert.IsType<TextBlock>(icon);
            Assert.Equal("\uE767", glyph.Text);
        });
    }

    private static AudioAppSession Session(string name, int pid, bool muted) =>
        new("session-1", "device", name, pid, ProcessPath: null, IconPath: null, Percent: muted ? 0 : 80, IsMuted: muted);

    private static DockPanel BuildRow(AudioAppSession session)
    {
        var flyout = new QuickSettingsFlyout();
        MethodInfo build = typeof(QuickSettingsFlyout).GetMethod(
            "BuildSessionRow", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (DockPanel)build.Invoke(flyout, [session])!;
    }

    /// <summary>Row = [icon host (Grid: icon + slash), percent, slider].</summary>
    private static (FrameworkElement Icon, FrameworkElement Slash) IconParts(DockPanel row)
    {
        Border host = Assert.IsType<Border>(row.Children[0]);
        Grid grid = Assert.IsType<Grid>(host.Child);
        Assert.Equal(2, grid.Children.Count);
        return (
            Assert.IsAssignableFrom<FrameworkElement>(grid.Children[0]),
            Assert.IsAssignableFrom<FrameworkElement>(grid.Children[1]));
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
        thread.Join();
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException($"STA thread failed: {error}");
        }
    }
}
