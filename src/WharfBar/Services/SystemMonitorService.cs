using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>Immutable snapshot of hardware resource usage (CPU, RAM, GPU).</summary>
internal sealed record SystemStatsSnapshot(
    int CpuPercent,
    string CpuText,
    string CpuTooltip,
    int RamPercent,
    double UsedRamGb,
    double TotalRamGb,
    string RamText,
    string RamTooltip,
    int GpuPercent,
    string GpuText,
    string GpuTooltip,
    bool IsGpuAvailable);

/// <summary>
/// Periodically monitors CPU, RAM, and GPU resource utilization using lightweight Win32 APIs
/// and performance counters without blocking the WPF UI thread.
/// </summary>
internal sealed class SystemMonitorService : IDisposable
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(1500);

    private readonly DispatcherTimer _timer;
    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;
    private bool _hasPrevCpuTime;
    private bool _disposed;
    private bool _gpuCounterFailed;

    internal SystemMonitorService()
    {
        Snapshot = BuildEmptySnapshot();

        _timer = new DispatcherTimer { Interval = PollingInterval };
        _timer.Tick += OnTimerTick;

        // Perform initial poll
        PollMetrics();
        _timer.Start();
    }

    internal SystemStatsSnapshot Snapshot { get; private set; }

    internal event EventHandler? Changed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        SystemStatsSnapshot next = await Task.Run(BuildSnapshot).ConfigureAwait(true);
        if (_disposed)
        {
            return;
        }

        Snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void PollMetrics()
    {
        Snapshot = BuildSnapshot();
    }

    private SystemStatsSnapshot BuildSnapshot()
    {
        // 1. CPU Usage via GetSystemTimes
        int cpuPercent = ReadCpuUsage();
        string cpuText = $"{cpuPercent}%";
        string cpuTooltip = $"CPU Kullanımı: %{cpuPercent}";

        // 2. RAM Usage via GlobalMemoryStatusEx
        (int ramPercent, double usedGb, double totalGb) = ReadRamUsage();
        string ramText = $"{ramPercent}%";
        string ramTooltip = string.Format(
            CultureInfo.CurrentCulture,
            "RAM Kullanımı: %{0} ({1:F1} GB / {2:F1} GB)",
            ramPercent,
            usedGb,
            totalGb);

        // 3. GPU Usage via PerformanceCounters
        int gpuPercent = ReadGpuUsage();
        bool isGpuAvailable = gpuPercent >= 0;
        string gpuText = isGpuAvailable ? $"{gpuPercent}%" : "N/A";
        string gpuTooltip = isGpuAvailable
            ? $"GPU Kullanımı: %{gpuPercent}"
            : "GPU Kullanımı: Kullanılamıyor";

        return new SystemStatsSnapshot(
            cpuPercent,
            cpuText,
            cpuTooltip,
            ramPercent,
            usedGb,
            totalGb,
            ramText,
            ramTooltip,
            gpuPercent,
            gpuText,
            gpuTooltip,
            isGpuAvailable);
    }

    private int ReadCpuUsage()
    {
        if (!SystemInfoNative.GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime))
        {
            return 0;
        }

        ulong idle = idleTime.ToTicks();
        ulong kernel = kernelTime.ToTicks();
        ulong user = userTime.ToTicks();

        if (!_hasPrevCpuTime)
        {
            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
            _hasPrevCpuTime = true;
            return 0;
        }

        ulong idleDelta = idle - _prevIdleTime;
        ulong kernelDelta = kernel - _prevKernelTime;
        ulong userDelta = user - _prevUserTime;
        ulong totalDelta = kernelDelta + userDelta;

        _prevIdleTime = idle;
        _prevKernelTime = kernel;
        _prevUserTime = user;

        if (totalDelta == 0)
        {
            return 0;
        }

        ulong usedDelta = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
        int percent = Math.Clamp((int)(usedDelta * 100 / totalDelta), 0, 100);
        return percent;
    }

    private static (int Percent, double UsedGb, double TotalGb) ReadRamUsage()
    {
        MEMORYSTATUSEX mem = MEMORYSTATUSEX.Create();
        if (!SystemInfoNative.GlobalMemoryStatusEx(ref mem))
        {
            return (0, 0, 0);
        }

        int percent = Math.Clamp((int)mem.dwMemoryLoad, 0, 100);
        double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
        double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);

        return (percent, usedGb, totalGb);
    }

    private int ReadGpuUsage()
    {
        if (_gpuCounterFailed)
        {
            return -1;
        }

        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                _gpuCounterFailed = true;
                return -1;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            string[] instanceNames = category.GetInstanceNames();
            float total3DUsage = 0;

            foreach (string instance in instanceNames)
            {
                if (instance.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    using var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                    total3DUsage += counter.NextValue();
                }
            }

            return Math.Clamp((int)Math.Round(total3DUsage), 0, 100);
        }
        catch
        {
            _gpuCounterFailed = true;
            return -1;
        }
    }

    private static SystemStatsSnapshot BuildEmptySnapshot() => new(
        0,
        "0%",
        "CPU",
        0,
        0,
        0,
        "0%",
        "RAM",
        -1,
        "N/A",
        "GPU",
        false);
}
