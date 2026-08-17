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

    /// <summary>DISPLAYCONFIG_TOPOLOGY_ID 1..4 aynı sırada ProjectionMode'a karşılık gelir.</summary>
    internal static ProjectionMode? FromTopologyId(uint id) => id switch
    {
        1 => ProjectionMode.InternalOnly,
        2 => ProjectionMode.Clone,
        3 => ProjectionMode.Extend,
        4 => ProjectionMode.ExternalOnly,
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
            Debug.WriteLine("WharfBar: QueryDisplayConfig failed");
            Current = null;
        }
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