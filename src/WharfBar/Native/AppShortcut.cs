using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Start Menü .lnk kısayollarından AppUserModelID okur. AUMID → Windows görünen adı
/// eşlemesi için (ör. "com.github.th-ch.youtube-music" → "YouTube Music",
/// "F0DC299D809B9700" → "Zen").
/// </summary>
internal static class AppShortcut
{
    private static readonly Guid PropertyStoreIid = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");

    /// <summary>
    /// Kısayolun PKEY_AppUserModel_ID değerini okur. Yoksa/okunamazsa false döner.
    /// </summary>
    internal static bool TryGetAppUserModelId(string lnkPath, out string? appUserModelId)
    {
        appUserModelId = null;

        Guid iid = PropertyStoreIid;
        int hr = NativeMethods.SHGetPropertyStoreFromParsingName(
            lnkPath, IntPtr.Zero, NativeMethods.GPS_DEFAULT, ref iid, out IntPtr store);
        if (hr != NativeMethods.S_OK || store == IntPtr.Zero)
        {
            return false;
        }

        IPropertyStore? props = null;
        try
        {
            props = (IPropertyStore)Marshal.GetObjectForIUnknown(store);
            var key = new PROPERTYKEY
            {
                fmtid = NativeMethods.PKEY_AppUserModelId,
                pid = 5,
            };

            hr = props.GetValue(ref key, out PROPVARIANT pv);
            if (hr == NativeMethods.S_OK && pv.vt == NativeMethods.VT_LPWSTR && pv.pwszVal != IntPtr.Zero)
            {
                appUserModelId = Marshal.PtrToStringUni(pv.pwszVal);
                NativeMethods.PropVariantClear(ref pv);
                return true;
            }

            if (pv.vt != NativeMethods.VT_EMPTY)
            {
                NativeMethods.PropVariantClear(ref pv);
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (props is not null)
            {
                Marshal.ReleaseComObject(props);
            }

            Marshal.Release(store);
        }
    }
}