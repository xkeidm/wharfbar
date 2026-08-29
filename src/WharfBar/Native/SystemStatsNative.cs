using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>Cumulative CPU times from <c>GetSystemTimes</c> (100 ns FILETIME ticks).</summary>
internal readonly struct CpuTimesSample
{
    internal ulong Idle { get; init; }
    internal ulong Kernel { get; init; }
    internal ulong User { get; init; }
}

/// <summary>Physical memory snapshot from <c>GlobalMemoryStatusEx</c>.</summary>
internal readonly struct MemoryStatusInfo
{
    internal ulong TotalPhys { get; init; }
    internal ulong AvailPhys { get; init; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
}

/// <summary>Thin wrappers around kernel32 CPU/memory queries and PDH GPU engine counters.</summary>
internal static class SystemStatsNative
{
    internal static CpuTimesSample QueryCpuTimes()
    {
        if (!GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            return default;
        }

        return new CpuTimesSample { Idle = idle, Kernel = kernel, User = user };
    }

    internal static MemoryStatusInfo QueryMemory()
    {
        var status = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>(),
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            return default;
        }

        return new MemoryStatusInfo { TotalPhys = status.ullTotalPhys, AvailPhys = status.ullAvailPhys };
    }

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern bool GetSystemTimes(
        out ulong lpIdleTime,
        out ulong lpKernelTime,
        out ulong lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Yazılımsal adaptörlerin (Microsoft Basic Render Driver / WARP) LUID anahtarlarını
    /// döndürür — PDH GPU Engine instance'larındaki <c>_luid_0xHHHHHHHH_0xLLLLLLLL</c>
    /// segmentiyle aynı biçimde. Bu adaptörler fiziksel GPU olmadığı için istatistik
    /// gösteriminde dışlanır (Görev Yöneticisi de listelemez). DXGI açılamazsa boş küme:
    /// davranış mevcut haline düşer (ekstra segment görünür, işlev bozulmaz).
    /// </summary>
    internal static IReadOnlySet<string> GetSoftwareAdapterLuidKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            Guid factoryIid = new("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (CreateDXGIFactory1(ref factoryIid, out IntPtr factory) != 0)
            {
                return keys;
            }

            try
            {
                for (uint i = 0; ; i++)
                {
                    int hr = EnumAdapters1(factory, i, out IntPtr adapter);
                    if (hr != 0)
                    {
                        break; // DXGI_ERROR_NOT_FOUND — liste sonu
                    }

                    try
                    {
                        if (GetDesc1(adapter, out DXGI_ADAPTER_DESC1 desc) == 0
                            && (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
                        {
                            keys.Add(FormattableString.Invariant(
                                $"0x{desc.AdapterLuid.HighPart:X8}_0x{desc.AdapterLuid.LowPart:X8}"));
                        }
                    }
                    finally
                    {
                        Marshal.Release(adapter);
                    }
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Software adapter enumeration failed — {ex.Message}");
        }

        return keys;
    }

    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 0x2;

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    // IDXGIFactory1::EnumAdapters1 — vtable slot 12: IUnknown(0-2) +
    // IDXGIObject SetPrivateData(3)/SetPrivateDataInterface(4)/GetPrivateData(5)/GetParent(6) +
    // IDXGIFactory EnumAdapters(7)/MakeWindowAssociation(8)/GetWindowAssociation(9)/
    // CreateSwapChain(10)/CreateSoftwareAdapter(11) → EnumAdapters1(12).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr factory, uint index, out IntPtr adapter);

    // IDXGIAdapter1::GetDesc1 — vtable slot 10: yukarıdaki 7 slot +
    // IDXGIAdapter EnumOutputs(7)/GetDesc(8)/CheckInterfaceSupport(9) → GetDesc1(10).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr adapter, out DXGI_ADAPTER_DESC1 desc);

    private static int EnumAdapters1(IntPtr factory, uint index, out IntPtr adapter)
    {
        IntPtr vtbl = Marshal.ReadIntPtr(factory);
        IntPtr fn = Marshal.ReadIntPtr(vtbl, 12 * IntPtr.Size);
        var func = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(fn);
        return func(factory, index, out adapter);
    }

    private static int GetDesc1(IntPtr adapter, out DXGI_ADAPTER_DESC1 desc)
    {
        IntPtr vtbl = Marshal.ReadIntPtr(adapter);
        IntPtr fn = Marshal.ReadIntPtr(vtbl, 10 * IntPtr.Size);
        var func = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(fn);
        return func(adapter, out desc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public LUID AdapterLuid;
        public uint Flags;
    }
}

/// <summary>
/// Live "\GPU Engine(*)\Utilization Percentage" wildcard query (pdh.dll). Uses
/// <c>PdhAddEnglishCounterW</c> so the path works on any OS language. Returns null from
/// <see cref="TryCreate"/> when GPU performance counters are unavailable (VM / driver).
/// </summary>
internal sealed class GpuUsageQuery : IDisposable
{
    private const string CounterPath = @"\GPU Engine(*)\Utilization Percentage";
    private const int PdhMoreData = unchecked((int)0x800007D2);
    private const uint PdhFmtDouble = 0x00000200;

    private IntPtr _query;
    private IntPtr _counter;
    private bool _primed;
    private bool _unusable;
    private bool _disposed;

    private GpuUsageQuery(IntPtr query, IntPtr counter)
    {
        _query = query;
        _counter = counter;
    }

    /// <summary>Opens the PDH query; null when the GPU engine counter set does not exist.</summary>
    internal static GpuUsageQuery? TryCreate()
    {
        try
        {
            if (PdhOpenQueryW(IntPtr.Zero, IntPtr.Zero, out IntPtr query) != 0)
            {
                return null;
            }

            if (PdhAddEnglishCounterW(query, CounterPath, IntPtr.Zero, out IntPtr counter) != 0)
            {
                _ = PdhCloseQuery(query);
                return null;
            }

            return new GpuUsageQuery(query, counter);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Collects and reads all instance values with their instance names (the name embeds
    /// the owning adapter's LUID). Utilization is an interval counter: the first
    /// collection primes the baseline and yields no data. Zero values are kept so fully
    /// idle adapters still report. A missing PDH export (unexpected shell/API set) marks
    /// the query unusable instead of throwing per tick.
    /// </summary>
    internal bool TrySample(out (string Name, double Value)[] instances)
    {
        instances = [];
        if (_unusable || _disposed || _query == IntPtr.Zero || _counter == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (PdhCollectQueryData(_query) != 0)
            {
                return false;
            }

            if (!_primed)
            {
                _primed = true;
                return false;
            }

            return TryReadInstances(out instances);
        }
        catch (EntryPointNotFoundException)
        {
            _unusable = true;
            return false;
        }
        catch (DllNotFoundException)
        {
            _unusable = true;
            return false;
        }
    }

    private bool TryReadInstances(out (string Name, double Value)[] instances)
    {
        instances = [];
        uint size = 4096;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            int rc;
            uint count;
            while (true)
            {
                rc = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out count, buffer);
                if (rc != PdhMoreData || size > 4 * 1024 * 1024)
                {
                    break;
                }

                // Required size reported in lpdwBufferSize — reallocate and retry.
                Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal((int)size);
            }

            if (rc != 0)
            {
                return false;
            }

            var result = new List<(string Name, double Value)>((int)count);
            int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM_W>();
            for (uint i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM_W>(
                    buffer + (int)(i * itemSize));
                if (item.FmtValue.CStatus is not (0x00 or 0x01)
                    || !IsPointerInBuffer(item.szName, buffer, size))
                {
                    continue;
                }

                string? name = Marshal.PtrToStringUni(item.szName);
                if (name is not null)
                {
                    result.Add((name, item.FmtValue.Value));
                }
            }

            instances = [.. result];
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_query != IntPtr.Zero)
        {
            _ = PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counter = IntPtr.Zero;
        }
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(IntPtr szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(
        IntPtr hQuery,
        string szFullCounterPath,
        IntPtr dwUserData,
        out IntPtr phCounter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr hQuery);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterArrayW(
        IntPtr hCounter,
        uint dwFormat,
        ref uint pdwBufferSize,
        out uint pdwItemCount,
        IntPtr itemBuffer);

    /// <summary>
    /// Native <c>PDH_FMT_COUNTERVALUE</c> (x64 ABI): <c>CStatus</c> at 0, 4 bytes padding,
    /// 8-byte union value at offset 8 → 16 bytes total. The value MUST sit at offset 8 —
    /// an 8-byte managed struct shrinks <c>PDH_FMT_COUNTERVALUE_ITEM_W</c> to 16, every
    /// item after the first then reads a garbage <c>szName</c> pointer.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)]
        public uint CStatus;

        [FieldOffset(8)]
        public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM_W
    {
        public IntPtr szName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }

    /// <summary>
    /// True when <paramref name="pointer"/> lies inside <c>[buffer, buffer+bufferSize)</c>.
    /// Guards <c>PtrToStringUni</c> against out-of-range instance-name pointers.
    /// </summary>
    internal static bool IsPointerInBuffer(IntPtr pointer, IntPtr buffer, uint bufferSize)
    {
        long start = buffer.ToInt64();
        return pointer.ToInt64() >= start && pointer.ToInt64() < start + bufferSize;
    }
}
