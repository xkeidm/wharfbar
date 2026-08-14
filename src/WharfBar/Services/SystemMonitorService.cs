using System.Diagnostics;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>Immutable snapshot of hardware resource usage (CPU, RAM, GPU).</summary>
internal sealed record SystemStatsSnapshot(
    int CpuPercent,
    double? CpuGhz,
    int RamPercent,
    double UsedRamGb,
    double TotalRamGb,
    bool IsGpuUsageAvailable,
    int GpuPercent,
    bool IsGpuVramAvailable,
    double? GpuUsedVramGb,
    double? GpuTotalVramGb);

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
    private bool _vramCounterFailed;

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
        int cpuPercent = ReadCpuUsage();
        (int ramPercent, double usedGb, double totalGb) = ReadRamUsage();
        int gpuPercent = ReadGpuUsage();
        bool isGpuUsageAvailable = gpuPercent >= 0;

        double? cpuGhz = SystemInfoNative.TryReadCpuGhz();
        bool vramOk = TryReadVram(out double vramUsed, out double vramTotal);
        return new SystemStatsSnapshot(
            cpuPercent,
            cpuGhz,
            ramPercent,
            usedGb,
            totalGb,
            isGpuUsageAvailable,
            isGpuUsageAvailable ? gpuPercent : 0,
            vramOk,
            vramOk ? vramUsed : null,
            vramOk ? vramTotal : null);
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

    private bool TryReadVram(out double usedGb, out double totalGb)
    {
        usedGb = 0;
        totalGb = 0;
        if (!DxgiAdapterMemory.TryReadTotal(out totalGb, out long adapterLuid) || totalGb <= 0)
        {
            return false;
        }

        return TryReadDedicatedUsageGb(adapterLuid, out usedGb);
    }

    private bool TryReadDedicatedUsageGb(long adapterLuid, out double usedGb)
    {
        usedGb = 0;
        if (_vramCounterFailed)
        {
            return false;
        }

        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Adapter Memory"))
            {
                _vramCounterFailed = true;
                return false;
            }

            var category = new PerformanceCounterCategory("GPU Adapter Memory");
            uint low = (uint)((ulong)adapterLuid & 0xFFFFFFFFu);
            uint high = (uint)((ulong)adapterLuid >> 32);
            string tokenA = $"luid_0x{high:X8}_0x{low:X8}_";
            string tokenB = $"luid_0x{low:X8}_0x{high:X8}_";

            float sum = 0;
            int matched = 0;
            foreach (string instance in category.GetInstanceNames())
            {
                if (!instance.Contains(tokenA, StringComparison.OrdinalIgnoreCase)
                    && !instance.Contains(tokenB, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var counter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instance);
                sum += counter.NextValue();
                matched++;
            }

            if (matched == 0)
            {
                return false;
            }

            usedGb = sum / (1024.0 * 1024.0 * 1024.0);
            return true;
        }
        catch
        {
            _vramCounterFailed = true;
            return false;
        }
    }

    private static SystemStatsSnapshot BuildEmptySnapshot() => new(
        0,
        null,
        0,
        0,
        0,
        false,
        0,
        false,
        null,
        null);
}
