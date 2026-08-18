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

    // IPropertyStore vtable: 0=QueryInterface 1=AddRef 2=Release 3=GetCount 4=GetAt
    // 5=GetValue 6=SetValue 7=Commit. GetValue: HRESULT(self, PROPERTYKEY*, PROPVARIANT*).
    private const int GetValueSlot = 5;

    /// <summary>
    /// Kısayolun PKEY_AppUserModel_ID değerini okur. Yoksa/okunamazsa false döner.
    /// COM marshal'ı buffer'ı bozduğu için PROPERTYKEY/PROPVARIANT elle (ham pointer) iletilir.
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

        IntPtr keyPtr = IntPtr.Zero;
        IntPtr pvPtr = IntPtr.Zero;
        try
        {
            IntPtr vtbl = Marshal.ReadIntPtr(store);
            IntPtr getValueFn = Marshal.ReadIntPtr(vtbl, GetValueSlot * IntPtr.Size);
            var getValue = Marshal.GetDelegateForFunctionPointer<GetValueNative>(getValueFn);

            keyPtr = BuildPropertyKey();
            pvPtr = Marshal.AllocHGlobal(16);

            hr = getValue(store, keyPtr, pvPtr);
            if (hr != NativeMethods.S_OK)
            {
                return false;
            }

            ushort vt = (ushort)Marshal.ReadInt16(pvPtr, 0);
            IntPtr pwszVal = Marshal.ReadIntPtr(pvPtr, 8);
            if (vt != NativeMethods.VT_LPWSTR || pwszVal == IntPtr.Zero)
            {
                return false;
            }

            appUserModelId = Marshal.PtrToStringUni(pwszVal);
            PropVariantClear(pvPtr);
            return appUserModelId is not null;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (pvPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pvPtr);
            }

            if (keyPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(keyPtr);
            }

            Marshal.Release(store);
        }
    }

    /// <summary>PROPERTYKEY = GUID (16B, <see cref="Guid.ToByteArray"/> düzeni) + uint pid (4B LE).</summary>
    private static IntPtr BuildPropertyKey()
    {
        IntPtr ptr = Marshal.AllocHGlobal(20);
        byte[] guidBytes = NativeMethods.PKEY_AppUserModelId.ToByteArray();
        Marshal.Copy(guidBytes, 0, ptr, 16);
        Marshal.WriteInt32(ptr, 16, unchecked((int)5));
        return ptr;
    }

    private static void PropVariantClear(IntPtr pvPtr)
    {
        ushort vt = (ushort)Marshal.ReadInt16(pvPtr, 0);
        if (vt == NativeMethods.VT_EMPTY)
        {
            return;
        }

        var pv = new PROPVARIANT
        {
            vt = vt,
            pwszVal = Marshal.ReadIntPtr(pvPtr, 8),
        };
        NativeMethods.PropVariantClear(ref pv);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetValueNative(IntPtr self, IntPtr key, IntPtr pv);
}