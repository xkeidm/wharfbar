using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Dock glyph: package logo for an AppUserModelId, shell icon for a file or folder,
/// stock recycle icon for Trash, letter glyph when the asset is missing.
/// </summary>
internal static class DockIconFactory
{
    internal static ImageSource Load(AppIconService icons, DockAppItem item, int sizePx)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(item);

        if (item.Kind == DockTileKind.Launchpad)
        {
            return LaunchpadGlyph.Create();
        }

        if (item.Kind == DockTileKind.Trash)
        {
            return StockOrLetter(item.Label);
        }

        string? aumid = PackageLogoSelection.TrySplitAumid(item.Id, out _, out _)
            ? item.Id
            : PackageLogoSelection.TrySplitAumid(item.Path, out _, out _)
                ? item.Path
                : null;
        if (aumid is not null)
        {
            return PackageIconResolver.TryLoad(aumid, item.Label, sizePx)
                ?? TrayLetterGlyph.Build(item.Label);
        }

        string? directory = item.Kind switch
        {
            DockTileKind.Downloads => string.IsNullOrWhiteSpace(item.Path) ? null : item.Path,
            DockTileKind.Folder => DockBuiltInTiles.ResolveDirectory(item.Path),
            _ => Directory.Exists(item.Path) ? item.Path : null,
        };

        if (directory is not null && TryShell(directory, sizePx, out ImageSource? folderIcon) && folderIcon is not null)
        {
            return folderIcon;
        }

        return icons.GetIcon(item.Path, sizePx);
    }

    private static ImageSource StockOrLetter(string label)
    {
        if (!RecycleBinNative.TryGetStockIcon(out IntPtr hIcon))
        {
            return TrayLetterGlyph.Build(label);
        }

        try
        {
            return FromIcon(hIcon);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar recycle icon failed: {ex.Message}");
            return TrayLetterGlyph.Build(label);
        }
        finally
        {
            ShellIcon.Destroy(hIcon);
        }
    }

    private static bool TryShell(string path, int sizePx, out ImageSource? source)
    {
        source = null;
        if (!ShellIcon.TryExtract(path, sizePx, out IntPtr hBitmap, out IntPtr hIcon))
        {
            return false;
        }

        try
        {
            source = hBitmap != IntPtr.Zero
                ? Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions())
                : FromIcon(hIcon);
            source.Freeze();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar dock shell icon failed: {ex.Message}");
            source = null;
            return false;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
            }

            ShellIcon.Destroy(hIcon);
        }
    }

    private static ImageSource FromIcon(IntPtr hIcon)
    {
        ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
            hIcon,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
