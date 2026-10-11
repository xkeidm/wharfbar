using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;
using WharfBar.Views;
using WharfBar.Views.Controls;
using Xunit;

namespace WharfBar.Tests;

public class DockStripPresenceTests
{
    [Fact]
    public void SetRows_FirstLayoutSnaps_LaterInsertStartsCollapsed()
    {
        Run(() =>
        {
            var strip = new DockStrip();
            DockItemViewModel first = Item(@"C:\Apps\A.exe");
            DockItemViewModel second = Item(@"C:\Apps\B.exe");

            strip.SetRows([first]);
            Assert.Equal(1d, strip.Cells[0].Presence, 3);
            Assert.False(strip.Cells[0].HasAnimatedProperties);
            Assert.Equal(DockPlateMetrics.StripWidth(1, 24d), strip.LiveWidth, 3);

            strip.SetRows([first, second]);

            Assert.Equal(2, strip.Cells.Count);
            Assert.Equal(1d, strip.Cells[0].Presence, 3);
            Assert.Equal(0d, strip.Cells[1].Presence, 3);
            Assert.True(strip.Cells[1].HasAnimatedProperties);
            Assert.Equal(2, strip.SettledSlotCount);
            Assert.Equal(DockPlateMetrics.StripWidth(1, 24d), strip.LiveWidth, 3);
        });
    }

    [Fact]
    public void SetRows_RemoveKeepsTheCellWhileItShrinks()
    {
        Run(() =>
        {
            var strip = new DockStrip { MotionEnabled = false };
            DockItemViewModel first = Item(@"C:\Apps\A.exe");
            DockItemViewModel second = Item(@"C:\Apps\B.exe");
            strip.SetRows([first, second]);

            strip.MotionEnabled = true;
            strip.SetRows([first]);

            Assert.Equal(2, strip.Cells.Count);
            Assert.True(strip.Cells[1].IsExiting);
            Assert.Equal(1, strip.SettledSlotCount);
            Assert.True(strip.Cells[1].HasAnimatedProperties);
            Assert.Equal(DockPlateMetrics.StripWidth(2, 24d), strip.LiveWidth, 3);
        });
    }

    [Fact]
    public void SetRows_RunningPinReusesTheSameCell()
    {
        Run(() =>
        {
            var strip = new DockStrip();
            DockItemViewModel closed = Item(@"C:\Apps\Note.exe", running: false);
            DockItemViewModel open = Item(@"C:\Apps\Note.exe", running: true, openOnDesktop: true);
            strip.SetRows([closed]);
            DockItemCell cell = strip.Cells[0];

            strip.SetRows([open]);

            Assert.Same(cell, strip.Cells[0]);
            Assert.Single(strip.Cells);
            Assert.False(cell.IsExiting);
            Assert.Equal(1d, cell.Presence, 3);
        });
    }

    private static DockItemViewModel Item(string path, bool running = false, bool openOnDesktop = false) =>
        DockItemViewModel.ForItem(
            new DockAppItem(
                running ? "aumid" : path,
                path,
                string.Empty,
                "App",
                IsPinned: true,
                IsRunning: running,
                running ? new IntPtr(4) : IntPtr.Zero,
                DockSection.Pinned,
                string.Empty,
                IsOpenOnDesktop: openOnDesktop),
            null!);

    private static void Run(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
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
                    // The control ctor may not have started a dispatcher.
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }
}
