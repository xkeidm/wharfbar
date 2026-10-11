using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;
using WharfBar.Views;
using WharfBar.Views.Controls;
using Xunit;

namespace WharfBar.Tests;

public class DockItemCellTests
{
    [Fact]
    public void RunningPill_IsAShortCapsuleOnlyWhileTheAppIsRunning()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var cell = new DockItemCell { MotionEnabled = false };
                cell.ApplyMetrics(glyphSize: 24d, buttonSize: 40d, height: 40d);

                cell.Row = Item(running: false);
                Assert.Equal(Visibility.Collapsed, cell.RunningPill.Visibility);

                cell.Row = Item(running: true, openOnDesktop: true);
                Assert.Equal(Visibility.Visible, cell.RunningPill.Visibility);
                Assert.Equal(16d, cell.RunningPill.Width);
                Assert.Equal(3d, cell.RunningPill.Height);
                Assert.Equal(2d, cell.RunningPill.Margin.Bottom);
                Assert.True(cell.RunningPill.CornerRadius.TopLeft >= cell.RunningPill.Height / 2d);
                Assert.True(cell.RunningPill.Width < 24d);
                // The line sits in the bottom lane, below the plate.
                Assert.True(cell.RunningPill.Margin.Bottom + cell.RunningPill.Height
                    < DockPlateMetrics.IndicatorLaneDip);

                // Running keeps the hover plate, and the plate stops above the line.
                Assert.Equal(Visibility.Visible, cell.HoverPlate.Visibility);
                Assert.Equal(32d, cell.HoverPlate.Width);
                Assert.Equal(32d, cell.HoverPlate.Height);
                Assert.Equal(4d, cell.HoverPlate.CornerRadius.TopLeft);
                Assert.True(cell.HoverPlate.Width > cell.IconGlyph.Width);
                Assert.Equal(8d, cell.IconGlyph.Margin.Bottom);

                cell.Row = Item(running: true, openOnDesktop: false);
                Assert.Equal(Visibility.Visible, cell.RunningPill.Visibility);
                Assert.Equal(8d, cell.RunningPill.Width);
                Assert.Equal(Visibility.Collapsed, cell.HoverPlate.Visibility);

                cell.Row = Item(running: true, openOnDesktop: true, windowCount: 1);
                Assert.Equal(Visibility.Collapsed, cell.WindowCountBadge.Visibility);

                cell.Row = Item(running: true, openOnDesktop: true, windowCount: 3);
                Assert.Equal(Visibility.Visible, cell.WindowCountBadge.Visibility);
                Assert.Equal("3", cell.WindowCountText.Text);
                Assert.Equal(Visibility.Visible, cell.RunningPill.Visibility);
                Assert.Equal(16d, cell.RunningPill.Width);

                cell.Row = Item(running: false);
                Assert.Equal(Visibility.Collapsed, cell.RunningPill.Visibility);
                Assert.Equal(Visibility.Collapsed, cell.WindowCountBadge.Visibility);
                Assert.Equal(Visibility.Collapsed, cell.HoverPlate.Visibility);

                cell.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = Mouse.MouseEnterEvent,
                });
                Assert.Equal(Visibility.Visible, cell.HoverPlate.Visibility);

                cell.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = Mouse.MouseLeaveEvent,
                });
                Assert.Equal(Visibility.Collapsed, cell.HoverPlate.Visibility);
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
                    // The cell ctor may not have started a dispatcher.
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

    [Fact]
    public void OpenChrome_FadesAndEasesWidthInsteadOfSnapping()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var cell = new DockItemCell { MotionEnabled = false };
                cell.ApplyMetrics(glyphSize: 24d, buttonSize: 40d, height: 40d);

                // Settle the closed pin first. Motion is armed only for the change under
                // test: a clock that has not ticked still reports its From value, so a
                // later hide must not be asked to read an opacity the show has not reached.
                cell.Row = Item(running: false);
                cell.MotionEnabled = true;
                cell.Row = Item(running: true, openOnDesktop: true);

                Assert.Equal(Visibility.Visible, cell.RunningPill.Visibility);
                Assert.Equal(Visibility.Visible, cell.HoverPlate.Visibility);
                Assert.True(cell.RunningPill.HasAnimatedProperties);
                Assert.True(cell.HoverPlate.HasAnimatedProperties);

                cell.MotionEnabled = false;
                cell.Row = Item(running: true, openOnDesktop: false);
                cell.MotionEnabled = true;
                cell.Row = Item(running: true, openOnDesktop: true, windowCount: 3);

                Assert.Equal(Visibility.Visible, cell.WindowCountBadge.Visibility);
                Assert.Equal("3", cell.WindowCountText.Text);
                Assert.True(cell.WindowCountBadge.HasAnimatedProperties);
                Assert.Equal(Visibility.Visible, cell.RunningPill.Visibility);
                Assert.True(cell.RunningPill.HasAnimatedProperties);

                cell.MotionEnabled = false;
                cell.Row = Item(running: true, openOnDesktop: true);
                cell.MotionEnabled = true;
                cell.Row = Item(running: false);

                Assert.Equal(Visibility.Visible, cell.RunningPill.Visibility);
                Assert.True(cell.RunningPill.HasAnimatedProperties);
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
                    // The cell ctor may not have started a dispatcher.
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

    private static DockItemViewModel Item(bool running, bool openOnDesktop = false, int windowCount = 0) =>
        DockItemViewModel.ForItem(
            new DockAppItem(
                "id",
                @"C:\Windows\System32\notepad.exe",
                string.Empty,
                "Notepad",
                IsPinned: true,
                IsRunning: running,
                IntPtr.Zero,
                DockSection.Pinned,
                string.Empty,
                IsOpenOnDesktop: openOnDesktop,
                WindowCount: windowCount),
            null!);
}
