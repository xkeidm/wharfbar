using System.Globalization;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>Immutable UI-facing system stats snapshot.</summary>
internal sealed record SystemStatsSnapshot(
    double? CpuPercent,
    double?[]? GpuPercents,
    double? RamPercent,
    IReadOnlyList<double>? CpuHistory = null,
    IReadOnlyList<double>? RamHistory = null,
    IReadOnlyList<double>? GpuHistory = null,
    string? NetworkDownText = null,
    string? NetworkUpText = null)
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
    /// <summary>
    /// GPU PDH wildcard collection is the expensive sample. It runs off the UI thread.
    /// </summary>
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(4);

    /// <summary>Sparkline window. A point is kept only when the visible readout changes.</summary>
    private const int SampleHistoryCap = 30;

    private readonly Dispatcher _dispatcher;
    private readonly Timer _timer;
    private readonly GpuUsageQuery? _gpu;
    private readonly IReadOnlySet<string> _softwareAdapterKeys;
    private readonly List<double> _cpuHistory = [];
    private readonly List<double> _ramHistory = [];
    private readonly List<double> _gpuHistory = [];
    private CpuTimesSample _prevCpu;
    private bool _havePrevCpu;
    private long _prevReceived;
    private long _prevSent;
    private DateTime _prevNetAt;
    private bool _haveNet;
    private volatile bool _sampling;
    private int _sampleBusy;
    private volatile bool _disposed;

    internal SystemStatsService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _gpu = GpuUsageQuery.TryCreate();
        _softwareAdapterKeys = SystemStatsNative.GetSoftwareAdapterLuidKeys();
        Snapshot = new SystemStatsSnapshot(null, null, null, [], [], [], "—", "—");
        _timer = new Timer(_ => SampleOnce(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal SystemStatsSnapshot Snapshot { get; private set; }

    internal event EventHandler? Changed;

    /// <summary>Starts or stops sampling. Hidden stats do not keep the PDH query collecting.</summary>
    internal void SetSampling(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        if (!enabled)
        {
            _sampling = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        if (_sampling)
        {
            return;
        }

        _sampling = true;
        _timer.Change(TimeSpan.Zero, SampleInterval);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sampling = false;
        _timer.Dispose();
        _gpu?.Dispose();
    }

    private void SampleOnce()
    {
        if (_disposed || !_sampling || Interlocked.Exchange(ref _sampleBusy, 1) != 0)
        {
            return;
        }

        try
        {
            double? cpu = SampleCpu();
            double? ram = SampleRam();
            double?[]? gpu = SampleGpu() ?? Snapshot.GpuPercents;
            (string down, string up) = SampleNetwork();
            var probe = new SystemStatsSnapshot(cpu, gpu, ram, null, null, null, down, up);
            if (SameReadout(Snapshot, probe))
            {
                return;
            }

            PushSample(_cpuHistory, cpu);
            PushSample(_ramHistory, ram);
            if (gpu is { Length: > 0 })
            {
                PushSample(_gpuHistory, SumGpuInstances([.. gpu.Select(static value => value ?? 0d)]));
            }

            var next = new SystemStatsSnapshot(
                cpu,
                gpu,
                ram,
                _cpuHistory.ToArray(),
                _ramHistory.ToArray(),
                _gpuHistory.ToArray(),
                down,
                up);
            Publish(next);
        }
        finally
        {
            Interlocked.Exchange(ref _sampleBusy, 0);
        }
    }

    private void Publish(SystemStatsSnapshot next)
    {
        if (_dispatcher.CheckAccess())
        {
            if (_disposed)
            {
                return;
            }

            Snapshot = next;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _ = _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            Snapshot = next;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// True when the top-bar text and the network-rate labels would not change.
    /// Sub-percent noise does not wake the UI or extend the sparkline.
    /// </summary>
    internal static bool SameReadout(SystemStatsSnapshot current, SystemStatsSnapshot next)
        => string.Equals(current.DisplayText(false), next.DisplayText(false), StringComparison.Ordinal)
           && string.Equals(current.DisplayText(true), next.DisplayText(true), StringComparison.Ordinal)
           && string.Equals(current.NetworkDownText, next.NetworkDownText, StringComparison.Ordinal)
           && string.Equals(current.NetworkUpText, next.NetworkUpText, StringComparison.Ordinal);

    private (string Down, string Up) SampleNetwork()
    {
        (long received, long sent) = NetworkRate.SumUpCounters();
        DateTime now = DateTime.UtcNow;
        double? down = null;
        double? up = null;
        if (_haveNet)
        {
            double elapsed = (now - _prevNetAt).TotalSeconds;
            down = NetworkRate.BytesPerSecond(_prevReceived, received, elapsed);
            up = NetworkRate.BytesPerSecond(_prevSent, sent, elapsed);
        }

        _prevReceived = received;
        _prevSent = sent;
        _prevNetAt = now;
        _haveNet = true;
        return (NetworkRate.FormatBytesPerSecond(down), NetworkRate.FormatBytesPerSecond(up));
    }

    private static void PushSample(List<double> history, double? value)
    {
        if (value is not double sample || double.IsNaN(sample) || double.IsInfinity(sample))
        {
            return;
        }

        history.Add(sample);
        if (history.Count > SampleHistoryCap)
        {
            history.RemoveAt(0);
        }
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
