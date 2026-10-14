using System.Diagnostics;
using WharfBar.Native;

namespace WharfBar.Services;

internal enum ProjectionMode
{
    InternalOnly,
    Clone,
    Extend,
    ExternalOnly,
}

internal sealed class ProjectionService
{
    internal ProjectionMode? Current { get; private set; }

    /// <summary>
    /// DISPLAYCONFIG_TOPOLOGY_ID gerçek değerleri (wingdi.h): INTERNAL=1, CLONE=2, EXTEND=4, EXTERNAL=8.
    /// </summary>
    internal static ProjectionMode? FromTopologyId(uint id) => id switch
    {
        1 => ProjectionMode.InternalOnly,
        2 => ProjectionMode.Clone,
        4 => ProjectionMode.Extend,
        8 => ProjectionMode.ExternalOnly,
        _ => null,
    };

    internal static uint SdcFlagFor(ProjectionMode mode) => mode switch
    {
        ProjectionMode.Clone => DisplayTopology.SDC_TOPOLOGY_CLONE,
        ProjectionMode.Extend => DisplayTopology.SDC_TOPOLOGY_EXTEND,
        ProjectionMode.ExternalOnly => DisplayTopology.SDC_TOPOLOGY_EXTERNAL,
        _ => DisplayTopology.SDC_TOPOLOGY_INTERNAL,
    };

    internal void Refresh()
    {
        if (DisplayTopology.TryGetCurrentTopology(out uint id))
        {
            Current = FromTopologyId(id);
        }
        else
        {
            // Veritabanı okunamazsa (bazı hibrit GPU'lar hâlâ 87 döner) geometriden tahmin et.
            Debug.WriteLine("WharfBar: QueryDisplayConfig failed — guessing from monitor metrics");
            (int monitors, int vx, int vy, int vw, int vh, int pw, int ph) = DisplayTopology.ReadMonitorMetrics();
            Current = GuessCurrentMode(monitors, vx, vy, vw, vh, pw, ph);
        }
    }

    /// <summary>
    /// Aktif monitör sayısı + sanal ekran geometrisinden mod tahmini.
    /// Tek monitör InternalOnly/ExternalOnly'yi ayırt edemez — InternalOnly varsayar.
    /// </summary>
    internal static ProjectionMode? GuessCurrentMode(
        int monitors, int virtualX, int virtualY, int virtualW, int virtualH,
        int primaryW, int primaryH)
    {
        if (monitors <= 0)
        {
            return null;
        }

        if (monitors == 1)
        {
            return ProjectionMode.InternalOnly;
        }

        bool singleArea = virtualW == primaryW && virtualH == primaryH;
        return singleArea ? ProjectionMode.Clone : ProjectionMode.Extend;
    }

    internal bool TryApply(ProjectionMode mode)
    {
        bool ok = DisplayTopology.TryApplyTopology(SdcFlagFor(mode));
        if (ok)
        {
            Current = mode;
        }
        else
        {
            Debug.WriteLine($"WharfBar: SetDisplayConfig failed for {mode}");
        }

        return ok;
    }
}