using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Native;
using Windows.Management.Deployment;

namespace WharfBar.Services;

/// <summary>
/// Resolves a packaged app's logo through <c>Windows.Management.Deployment</c> and, when the
/// logo is an indirect resource, <c>SHLoadIndirectString</c>. A missing asset becomes the
/// letter glyph — never a broken image path.
/// </summary>
internal static class PackageIconResolver
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static ImageSource? TryLoad(string? aumid, string? label, int sizePx)
    {
        if (!PackageLogoSelection.TrySplitAumid(aumid, out string family, out _))
        {
            return null;
        }

        int px = Math.Clamp(sizePx, 16, 256);
        string key = family + "\0" + px;
        return Cache.GetOrAdd(key, _ => Load(family, label));
    }

    private static ImageSource Load(string familyName, string? label)
    {
        try
        {
            if (TryReadPackage(familyName, out string? fullName, out string? install, out string? logo))
            {
                string? indirect = PackageLogoSelection.IndirectSource(fullName, logo);
                if (indirect is not null
                    && PackageIconNative.TryLoadIndirectString(indirect, out string resolved)
                    && File.Exists(resolved))
                {
                    ImageSource? decoded = TryDecodeFile(resolved);
                    if (decoded is not null)
                    {
                        return decoded;
                    }
                }

                string? file = PackageLogoSelection.FirstExisting(
                    PackageLogoSelection.CandidateFiles(install, logo),
                    File.Exists);
                if (file is not null)
                {
                    ImageSource? decoded = TryDecodeFile(file);
                    if (decoded is not null)
                    {
                        return decoded;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar package icon failed for {familyName}: {ex.Message}");
        }

        return TrayLetterGlyph.Build(label);
    }

    private static bool TryReadPackage(
        string familyName,
        out string? fullName,
        out string? installLocation,
        out string? logo)
    {
        fullName = null;
        installLocation = null;
        logo = null;
        var manager = new PackageManager();
        foreach (Windows.ApplicationModel.Package package in manager.FindPackagesForUser(string.Empty, familyName))
        {
            try
            {
                fullName = package.Id?.FullName;
                installLocation = package.InstalledLocation?.Path;
                logo = package.Logo?.OriginalString;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar package logo metadata failed: {ex.Message}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(logo) || !string.IsNullOrWhiteSpace(installLocation))
            {
                return true;
            }
        }

        return false;
    }

    private static ImageSource? TryDecodeFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar package logo file failed: {ex.Message}");
            return null;
        }
    }
}
