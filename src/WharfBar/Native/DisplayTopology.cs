using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class DisplayTopology
{
    internal const uint SDC_TOPOLOGY_INTERNAL = 0x1;
    internal const uint SDC_TOPOLOGY_CLONE = 0x2;
    internal const uint SDC_TOPOLOGY_EXTEND = 0x4;
    internal const uint SDC_TOPOLOGY_EXTERNAL = 0x8;
    internal const uint SDC_APPLY = 0x80;
    internal const uint SDC_ALLOW_CHANGES = 0x400;
    /// <summary>
    /// winuser.h: 0x4. 0x2 is <c>QDC_ONLY_ACTIVE_PATHS</c>; that flag plus a topology
    /// pointer returns ERROR_INVALID_PARAMETER.
    /// </summary>
    internal const uint QDC_DATABASE_CURRENT = 0x4;
    internal const uint QDC_VIRTUAL_MODE_AWARE = 0x10;
    private const int ERROR_SUCCESS = 0;

    // Native sizes that contain no pointers. QueryDisplayConfig returned 0 on this machine.
    private const int PathInfoBytes = 72;
    private const int ModeInfoBytes = 64;

    /// <summary>
    /// Current topology id. A zero buffer returns 87 on this system; <c>GetDisplayConfigBufferSizes</c>
    /// is needed first, then the real buffer.
    /// </summary>
    internal static int QueryCurrentTopology(out uint topologyId)
    {
        topologyId = 0;
        uint flags = QDC_DATABASE_CURRENT | QDC_VIRTUAL_MODE_AWARE;
        int sized = GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
        if (sized != ERROR_SUCCESS || pathCount == 0 || modeCount == 0)
        {
            return sized == ERROR_SUCCESS ? 87 : sized;
        }

        IntPtr paths = Marshal.AllocHGlobal(PathInfoBytes * (int)pathCount);
        IntPtr modes = Marshal.AllocHGlobal(ModeInfoBytes * (int)modeCount);
        try
        {
            return QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, ref topologyId);
        }
        finally
        {
            Marshal.FreeHGlobal(paths);
            Marshal.FreeHGlobal(modes);
        }
    }

    internal static bool TryGetCurrentTopology(out uint topologyId)
    {
        int status = QueryCurrentTopology(out topologyId);
        return status == ERROR_SUCCESS && topologyId is 1 or 2 or 4 or 8;
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_CMONITORS = 80;

    /// <summary>Active monitor count + virtual/primary screen geometry (GuessCurrentMode input).</summary>
    internal static (int Monitors, int VX, int VY, int VW, int VH, int PW, int PH) ReadMonitorMetrics() =>
        (
            GetSystemMetrics(SM_CMONITORS),
            GetSystemMetrics(SM_XVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_CYVIRTUALSCREEN),
            GetSystemMetrics(SM_CXSCREEN),
            GetSystemMetrics(SM_CYSCREEN)
        );

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>
    /// The only combination proven reliable for changing topology:
    /// <c>topologyFlag | SDC_APPLY</c>. Every extra flag added (SUPPLIED / ALLOW_CHANGES /
    /// SAVE_TO_DATABASE) returns ERROR_INVALID_PARAMETER on this machine.
    /// </summary>
    internal static bool TryApplyTopology(uint topologyFlag) =>
        SetDisplayConfig(
            0, IntPtr.Zero,
            0, IntPtr.Zero,
            topologyFlag | SDC_APPLY) == ERROR_SUCCESS;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        IntPtr pathArray,
        ref uint numModeInfoArrayElements,
        IntPtr modeInfoArray,
        ref uint currentTopologyId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetDisplayConfig(
        int numPathArrayElements,
        IntPtr pathArray,
        int numModeInfoArrayElements,
        IntPtr modeInfoArray,
        uint flags);
}