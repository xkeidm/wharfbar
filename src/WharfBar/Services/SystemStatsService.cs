using System.Globalization;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>Immutable UI-facing system stats snapshot.</summary>
internal sealed record SystemStatsSnapshot(double? CpuPercent, double?[]? GpuPercents, double? RamPercent)
{
    /// <summary>Top-bar text; <paramref name="showGpuPerAdapter"/> numbers segments when two or more adapters report.</summary>
    internal string DisplayText(bool showGpuPerAdapter)
        => SystemStatsService.FormatText(CpuPercent, GpuPercents, RamPercent, showGpuPerAdapter);

    /// <summary>Mode-independent canonical value string for change detection (array identity safe).</summary>
    internal string ChangeKey => string.Create(
        CultureInfo.InvariantCulture,
        $"C{FormatNullable(CpuPercent)}|G{string.Join(",", (GpuPercents ?? []).Select(FormatNullable))}|R{FormatNullable(RamPercent)}");

    private static string FormatNullable(double? value)
        => value?.ToString("R", CultureInfo.InvariantCulture) ?? "-";
}

/// <summary>
/// Samples CPU / GPU / RAM usage for the top-bar system-stats readout.
/// Pure computation helpers are static and unit-tested; sampling uses
/// documented Win32 APIs only (no external packages).
/// Shared by every top-bar window via <see cref="TopBarEngine"/> (like <see cref="StatusService"/>).
/// </summary>
internal sealed class SystemStatsService : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _timer;
    private readonly GpuUsageQuery? _gpu;
    private readonly IReadOnlySet<string> _softwareAdapterKeys;
    private CpuTimesSample _prevCpu;
    private bool _havePrevCpu;
    private bool _disposed;

    internal SystemStatsService()
    {
        // Created on the UI thread (TopBarEngine ctor) so the timer ticks there.
        _gpu = GpuUsageQuery.TryCreate();
        _softwareAdapterKeys = SystemStatsNative.GetSoftwareAdapterLuidKeys();
        Snapshot = new SystemStatsSnapshot(null, null, null);

        _timer = new DispatcherTimer { Interval = SampleInterval };
        _timer.Tick += (_, _) => SampleOnce();
    }

    internal SystemStatsSnapshot Snapshot { get; private set; }

    internal event EventHandler? Changed;

    internal void Start()
    {
        if (_disposed || _timer.IsEnabled)
        {
            return;
        }

        SampleOnce();
        _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _gpu?.Dispose();
    }

    private void SampleOnce()
    {
        double? cpu = SampleCpu();
        double? ram = SampleRam();
        double?[]? gpu = SampleGpu() ?? Snapshot.GpuPercents;

        SystemStatsSnapshot next = Snapshot with { CpuPercent = cpu, GpuPercents = gpu, RamPercent = ram };
        if (next.ChangeKey == Snapshot.ChangeKey)
        {
            return;
        }

        Snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>CPU busy percentage since the previous sample; null until two samples exist.</summary>
    private double? SampleCpu()
    {
        CpuTimesSample now = SystemStatsNative.QueryCpuTimes();
        double? percent = _havePrevCpu
            ? ComputeCpuUsage(
                now.Idle, now.Kernel, now.User,
                _prevCpu.Idle, _prevCpu.Kernel, _prevCpu.User)
            : null;

        _prevCpu = now;
        _havePrevCpu = true;
        return percent;
    }

    private static double? SampleRam()
    {
        MemoryStatusInfo memory = SystemStatsNative.QueryMemory();
        return memory.TotalPhys == 0
            ? null
            : ComputeRamUsage(memory.TotalPhys, memory.AvailPhys);
    }

    private double?[]? SampleGpu()
    {
        if (_gpu is null || !_gpu.TrySample(out (string Name, double Value)[] instances))
        {
            return null;
        }

        return [.. GroupByAdapters(instances, _softwareAdapterKeys).Select(static value => (double?)value)];
    }

    /// <summary>
    /// Busy share of the elapsed CPU time between two <c>GetSystemTimes</c> samples.
    /// Kernel time includes idle, so total = kernel + user; result clamped to 0–100.
    /// </summary>
    internal static double ComputeCpuUsage(
        ulong idle,
        ulong kernel,
        ulong user,
        ulong prevIdle,
        ulong prevKernel,
        ulong prevUser)
    {
        ulong totalDelta = (kernel + user) - (prevKernel + prevUser);
        if (totalDelta == 0)
        {
            return 0d;
        }

        ulong idleDelta = idle - prevIdle;
        if (idleDelta >= totalDelta)
        {
            return 0d;
        }

        double busy = 100d * (1d - ((double)idleDelta / totalDelta));
        return Math.Clamp(busy, 0d, 100d);
    }

    /// <summary>Used physical memory percentage, clamped to 0–100.</summary>
    internal static double ComputeRamUsage(ulong totalPhys, ulong availPhys)
    {
        if (totalPhys == 0 || availPhys >= totalPhys)
        {
            return 0d;
        }

        return 100d * ((double)(totalPhys - availPhys) / totalPhys);
    }

    /// <summary>Sums per-instance GPU engine utilization percentages, clamped to 0–100.</summary>
    internal static double SumGpuInstances(double[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return 0d;
        }

        double sum = 0d;
        foreach (double value in values)
        {
            sum += value;
        }

        return Math.Clamp(sum, 0d, 100d);
    }

    /// <summary>
    /// Adapter identity embedded in a GPU Engine counter instance name, e.g.
    /// <c>pid_14180_luid_0x00000000_0x0000C3B7_phys_0_eng_0_engtype_3D</c> →
    /// <c>0x00000000_0x0000C3B7</c>. Null when the name has no recognizable LUID segment.
    /// </summary>
    internal static string? ParseAdapterKey(string? instanceName)
    {
        if (string.IsNullOrEmpty(instanceName))
        {
            return null;
        }

        const string marker = "_luid_";
        int start = instanceName.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        int end = instanceName.IndexOf("_phys_", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = instanceName.Length;
        }

        return start < end ? instanceName[start..end] : null;
    }

    /// <summary>
    /// Sums engine utilization per physical adapter (grouped by LUID), each clamped to
    /// 0–100, ordered by adapter key. Instances without a parseable adapter key are
    /// skipped so unknown formats never fabricate an adapter. Software adapters
    /// (Microsoft Basic Render Driver / WARP — <paramref name="softwareKeys"/>) are
    /// excluded: Windows exposes them as real D3D adapters but they are not physical
    /// GPUs and Task Manager does not list them either.
    /// </summary>
    internal static double[] GroupByAdapters((string Name, double Value)[]? instances, IReadOnlySet<string>? softwareKeys = null)
    {
        if (instances is null || instances.Length == 0)
        {
            return [];
        }

        SortedDictionary<string, List<double>> groups = new(StringComparer.Ordinal);
        foreach ((string name, double value) in instances)
        {
            string? key = ParseAdapterKey(name);
            if (key is null || softwareKeys?.Contains(key) == true)
            {
                continue;
            }

            if (!groups.TryGetValue(key, out List<double>? bucket))
            {
                bucket = [];
                groups.Add(key, bucket);
            }

            bucket.Add(value);
        }

        return groups.Values.Select(static bucket => SumGpuInstances([.. bucket])).ToArray();
    }

    /// <summary>
    /// Compact top-bar text, e.g. <c>CPU 12% · GPU 8% · RAM 47%</c>. With
    /// <paramref name="showGpuPerAdapter"/> and two or more adapters, GPU segments are
    /// numbered: <c>GPU0 4% · GPU1 24%</c>. Missing metrics drop their segment; no
    /// samples at all → <c>--</c>.
    /// </summary>
    internal static string FormatText(
        double? cpuPercent,
        double?[]? gpuPercents,
        double? ramPercent,
        bool showGpuPerAdapter)
    {
        List<string> segments = [];
        AppendSegment(segments, "CPU", cpuPercent);
        AppendGpuSegments(segments, gpuPercents, showGpuPerAdapter);
        AppendSegment(segments, "RAM", ramPercent);

        return segments.Count == 0 ? "--" : string.Join(" · ", segments);
    }

    private static void AppendGpuSegments(List<string> segments, double?[]? gpuPercents, bool showGpuPerAdapter)
    {
        if (gpuPercents is not { Length: > 0 })
        {
            return;
        }

        if (showGpuPerAdapter && gpuPercents.Length > 1)
        {
            for (int i = 0; i < gpuPercents.Length; i++)
            {
                AppendSegment(segments, $"GPU{i}", gpuPercents[i]);
            }

            return;
        }

        AppendSegment(segments, "GPU", SumGpuInstances([.. gpuPercents.Select(static value => value ?? 0d)]));
    }

    private static void AppendSegment(List<string> segments, string label, double? percent)
    {
        if (!percent.HasValue)
        {
            return;
        }

        int rounded = (int)Math.Round(percent.Value, MidpointRounding.AwayFromZero);
        segments.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{label} {rounded}%"));
    }
}
