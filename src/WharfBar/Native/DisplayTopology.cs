using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class DisplayTopology
{
    internal const uint SDC_TOPOLOGY_INTERNAL = 0x1;
    internal const uint SDC_TOPOLOGY_CLONE = 0x2;
    internal const uint SDC_TOPOLOGY_EXTEND = 0x4;
    internal const uint SDC_TOPOLOGY_EXTERNAL = 0x8;
    internal const uint SDC_APPLY = 0x80;
    internal const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
    internal const uint SDC_ALLOW_CHANGES = 0x400;
    internal const uint QDC_DATABASE_CURRENT = 0x2;
    private const int ERROR_SUCCESS = 0;

    /// <summary>Mevcut topoloji ID'sini sıfır buffer ile okur (belgelenmiş kullanım).</summary>
    internal static bool TryGetCurrentTopology(out uint topologyId)
    {
        topologyId = 0;
        return QueryDisplayConfig(
            QDC_DATABASE_CURRENT,
            0, IntPtr.Zero,
            0, IntPtr.Zero,
            ref topologyId) == ERROR_SUCCESS;
    }

    internal static bool TryApplyTopology(uint flags) =>
        SetDisplayConfig(
            0, IntPtr.Zero,
            0, IntPtr.Zero,
            flags | SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES) == ERROR_SUCCESS;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int QueryDisplayConfig(
        uint flags,
        int numPathArrayElements,
        IntPtr pathArray,
        int numModeInfoArrayElements,
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