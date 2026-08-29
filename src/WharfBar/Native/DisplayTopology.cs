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

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_CMONITORS = 80;

    /// <summary>Aktif monitör sayısı + sanal/primary ekran geometrisi (GuessCurrentMode girdisi).</summary>
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
    /// Topoloji değiştirmenin kanıtlanmış tek güvenilir kombinasyonu:
    /// <c>topologyFlag | SDC_APPLY</c>. Eklenen her ekstra flag (SUPPLIED / ALLOW_CHANGES /
    /// SAVE_TO_DATABASE) bu makinede ERROR_INVALID_PARAMETER döndürür.
    /// </summary>
    internal static bool TryApplyTopology(uint topologyFlag) =>
        SetDisplayConfig(
            0, IntPtr.Zero,
            0, IntPtr.Zero,
            topologyFlag | SDC_APPLY) == ERROR_SUCCESS;

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