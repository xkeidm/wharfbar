using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WharfBar.Models;
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
                var cell = new DockItemCell();
                cell.ApplyMetrics(glyphSize: 24d, buttonSize: 40d, height: 40d);

                cell.Row = Item(running: false);
                Assert.Equal(Visibility.Collapsed, cell.RunningPill.Visibility);

                cell.Row = Item(running: true);
                Assert.Equal(Visibility.Visible, cell.RunningPill.Visibility);
                Assert.Equal(8d, cell.RunningPill.Width);
                Assert.Equal(3d, cell.RunningPill.Height);
                Assert.True(cell.RunningPill.CornerRadius.TopLeft >= cell.RunningPill.Height / 2d);
                Assert.True(cell.RunningPill.Width < 24d);

                // Hover mark fills the button, not the glyph.
                Assert.Equal(Visibility.Collapsed, cell.HoverPlate.Visibility);
                Assert.Equal(40d, cell.HoverPlate.Width);
                Assert.Equal(40d, cell.HoverPlate.Height);
                Assert.Equal(4d, cell.HoverPlate.CornerRadius.TopLeft);
                Assert.True(cell.HoverPlate.Width > cell.IconGlyph.Width);

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

    private static DockItemViewModel Item(bool running) =>
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
                string.Empty),
            null!);
}
