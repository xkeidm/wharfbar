using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class DxgiAdapterMemory
{
    private static readonly Guid Factory1Iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid Adapter3Iid = new("645967a4-1392-4310-a798-8053ce3e93fd");

    private const int DxgiAdapterFlagSoftware = 2;
    private const int DxgiMemorySegmentGroupLocal = 0;
    private const uint CoInitMultithreaded = 0;
    private const int CoENotInitialized = unchecked((int)0x800401F0);
    private const double BytesPerGb = 1024.0 * 1024.0 * 1024.0;

    /// <summary>
    /// Adapter-wide dedicated size (not this process). Used bytes come from
    /// <c>GPU Adapter Memory</c> counters — <c>QueryVideoMemoryInfo.CurrentUsage</c>
    /// is per-process WDDM budget and is ~0 without a D3D device.
    /// </summary>
    internal static bool TryReadTotal(out double totalGb, out long adapterLuid)
    {
        totalGb = 0;
        adapterLuid = 0;
        try
        {
            bool didInit = false;
            int hr = CreateDXGIFactory1(in Factory1Iid, out IDXGIFactory1? factory);
            if (hr == CoENotInitialized)
            {
                int initHr = CoInitializeEx(IntPtr.Zero, CoInitMultithreaded);
                didInit = initHr == NativeMethods.S_OK || initHr == 1;
                hr = CreateDXGIFactory1(in Factory1Iid, out factory);
            }

            try
            {
                if (hr != NativeMethods.S_OK || factory is null)
                {
                    return false;
                }

                nuint bestDedicated = 0;
                double bestTotal = 0;
                long bestLuid = 0;
                bool found = false;

                for (uint i = 0; ; i++)
                {
                    int enumHr = factory.EnumAdapters1(i, out IDXGIAdapter1? adapter1);
                    if (enumHr != NativeMethods.S_OK || adapter1 is null)
                    {
                        break;
                    }

                    try
                    {
                        if (adapter1.GetDesc1(out DXGI_ADAPTER_DESC1 desc) != NativeMethods.S_OK)
                        {
                            continue;
                        }

                        if ((desc.Flags & DxgiAdapterFlagSoftware) != 0
                            || desc.Description.Contains("Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        nuint dedicated = desc.DedicatedVideoMemory;
                        double dedicatedGb = dedicated / BytesPerGb;
                        double budgetGb = 0;

                        IntPtr unk = Marshal.GetIUnknownForObject(adapter1);
                        Guid iid = Adapter3Iid;
                        int qi = Marshal.QueryInterface(unk, ref iid, out IntPtr adapter3Ptr);
                        Marshal.Release(unk);
                        if (qi == NativeMethods.S_OK && adapter3Ptr != IntPtr.Zero)
                        {
                            IDXGIAdapter3 adapter3 = (IDXGIAdapter3)Marshal.GetObjectForIUnknown(adapter3Ptr);
                            Marshal.Release(adapter3Ptr);
                            try
                            {
                                if (adapter3.QueryVideoMemoryInfo(
                                        0,
                                        DxgiMemorySegmentGroupLocal,
                                        out DXGI_QUERY_VIDEO_MEMORY_INFO mem) == NativeMethods.S_OK)
                                {
                                    budgetGb = mem.Budget / BytesPerGb;
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(adapter3);
                            }
                        }

                        double total = dedicatedGb > 0 ? dedicatedGb : budgetGb;
                        if (total <= 0 || dedicated < bestDedicated)
                        {
                            continue;
                        }

                        bestDedicated = dedicated;
                        bestTotal = total;
                        bestLuid = desc.AdapterLuid;
                        found = true;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter1);
                    }
                }

                if (!found)
                {
                    return false;
                }

                totalGb = bestTotal;
                adapterLuid = bestLuid;
                return true;
            }
            finally
            {
                if (factory is not null)
                {
                    Marshal.ReleaseComObject(factory);
                }

                if (didInit)
                {
                    CoUninitialize();
                }
            }
        }
        catch
        {
            totalGb = 0;
            adapterLuid = 0;
            return false;
        }
    }

    [DllImport("dxgi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int CreateDXGIFactory1(
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1? factory);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_QUERY_VIDEO_MEMORY_INFO
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
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
        public long AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
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
        public long AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC2
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
        public long AdapterLuid;
        public uint Flags;
        public int GraphicsPreemptionGranularity;
        public int ComputePreemptionGranularity;
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        int GetPrivateData(ref Guid name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, out IntPtr ppParent);
        int EnumAdapters(uint adapter, out IntPtr ppAdapter);
        int MakeWindowAssociation(IntPtr windowHandle, uint flags);
        int GetWindowAssociation(out IntPtr pWindowHandle);
        int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
        int CreateSoftwareAdapter(IntPtr module, out IntPtr ppAdapter);

        [PreserveSig]
        int EnumAdapters1(uint adapter, out IDXGIAdapter1 ppAdapter);

        [PreserveSig]
        int IsCurrent();
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        int GetPrivateData(ref Guid name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, out IntPtr ppParent);
        int EnumOutputs(uint output, out IntPtr ppOutput);
        int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        int CheckInterfaceSupport(ref Guid interfaceName, out long pUmdVersion);

        [PreserveSig]
        int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
    }

    [ComImport]
    [Guid("645967a4-1392-4310-a798-8053ce3e93fd")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter3
    {
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        int GetPrivateData(ref Guid name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, out IntPtr ppParent);
        int EnumOutputs(uint output, out IntPtr ppOutput);
        int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        int CheckInterfaceSupport(ref Guid interfaceName, out long pUmdVersion);
        int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
        int GetDesc2(out DXGI_ADAPTER_DESC2 pDesc);
        int RegisterHardwareContentProtectionTeardownStatusEvent(IntPtr hEvent, out uint pdwCookie);
        int UnregisterHardwareContentProtectionTeardownStatusEvent(uint dwCookie);

        [PreserveSig]
        int QueryVideoMemoryInfo(
            uint nodeIndex,
            int memorySegmentGroup,
            out DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);
    }
}
