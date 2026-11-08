using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class SystemStatsTests
{
    [Fact]
    public void ComputeCpuUsage_ReturnsBusyShare_OfTotalDelta()
    {
        // idle = 2_500 of 10_000 total ticks → 75% busy
        double usage = SystemStatsService.ComputeCpuUsage(
            idle: 2_500, kernel: 7_500, user: 2_500,
            prevIdle: 0, prevKernel: 0, prevUser: 0);
        Assert.Equal(75.0, usage, precision: 1);
    }

    [Fact]
    public void ComputeCpuUsage_IdleOnlyDelta_ReturnsZero()
    {
        double usage = SystemStatsService.ComputeCpuUsage(
            idle: 10_000, kernel: 10_000, user: 0,
            prevIdle: 0, prevKernel: 0, prevUser: 0);
        Assert.Equal(0.0, usage, precision: 1);
    }

    [Fact]
    public void ComputeCpuUsage_ClampsAbove100()
    {
        // idle delta larger than total delta (clock skew) must not exceed bounds
        double usage = SystemStatsService.ComputeCpuUsage(
            idle: 0, kernel: 5_000, user: 0,
            prevIdle: 4_000, prevKernel: 5_000, prevUser: 0);
        Assert.Equal(0.0, usage, precision: 1);
    }

    [Fact]
    public void ComputeCpuUsage_ZeroElapsed_ReturnsZero()
    {
        double usage = SystemStatsService.ComputeCpuUsage(0, 0, 0, 0, 0, 0);
        Assert.Equal(0.0, usage, precision: 1);
    }

    [Fact]
    public void ComputeRamUsage_HalfAvailable_Returns50()
    {
        double usage = SystemStatsService.ComputeRamUsage(totalPhys: 16_000UL, availPhys: 8_000UL);
        Assert.Equal(50.0, usage, precision: 1);
    }

    [Fact]
    public void ComputeRamUsage_ZeroTotal_ReturnsZero()
    {
        double usage = SystemStatsService.ComputeRamUsage(totalPhys: 0, availPhys: 0);
        Assert.Equal(0.0, usage, precision: 1);
    }

    [Fact]
    public void ComputeRamUsage_AvailExceedsTotal_ClampsToZero()
    {
        double usage = SystemStatsService.ComputeRamUsage(totalPhys: 1_000UL, availPhys: 2_000UL);
        Assert.Equal(0.0, usage, precision: 1);
    }

    [Fact]
    public void SumGpuInstances_SumsAllInstances()
    {
        double total = SystemStatsService.SumGpuInstances([10.0, 5.0, 3.25]);
        Assert.Equal(18.25, total, precision: 2);
    }

    [Fact]
    public void SumGpuInstances_ClampsTo100()
    {
        double total = SystemStatsService.SumGpuInstances([80.0, 60.0]);
        Assert.Equal(100.0, total, precision: 1);
    }

    [Fact]
    public void SumGpuInstances_EmptyOrNull_ReturnsZero()
    {
        Assert.Equal(0.0, SystemStatsService.SumGpuInstances(null), precision: 1);
        Assert.Equal(0.0, SystemStatsService.SumGpuInstances(Array.Empty<double>()), precision: 1);
    }

    // --- Adapter key parsing (PDH "\GPU Engine(*)\Utilization Percentage") ---

    [Fact]
    public void ParseAdapterKey_ExtractsLuidSegment()
    {
        string? key = SystemStatsService.ParseAdapterKey(
            "pid_14180_luid_0x00000000_0x0000C3B7_phys_0_eng_0_engtype_3D");
        Assert.Equal("0x00000000_0x0000C3B7", key);
    }

    [Fact]
    public void ParseAdapterKey_MultipleEngines_SameKey()
    {
        string? a = SystemStatsService.ParseAdapterKey(
            "pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D");
        string? b = SystemStatsService.ParseAdapterKey(
            "pid_2_luid_0x00000000_0x0000AAAA_phys_0_eng_3_engtype_Copy");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ParseAdapterKey_NoLuidMarker_ReturnsNull()
    {
        Assert.Null(SystemStatsService.ParseAdapterKey("pid_123_phys_0_eng_0"));
        Assert.Null(SystemStatsService.ParseAdapterKey(null));
        Assert.Null(SystemStatsService.ParseAdapterKey(""));
    }

    [Fact]
    public void ParseAdapterKey_MissingPhysSuffix_TakesRemainder()
    {
        string? key = SystemStatsService.ParseAdapterKey(
            "pid_9_luid_0x00000000_0x0000BBBB");
        Assert.Equal("0x00000000_0x0000BBBB", key);
    }

    // --- Grouping instances per physical adapter ---

    [Fact]
    public void GroupByAdapters_GroupsByLuid_AndOrdersByKey()
    {
        (string, double)[] instances =
        [
            ("pid_1_luid_0x00000000_0x0000BBBB_phys_0_eng_0_engtype_3D", 20.0),
            ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 4.0),
            ("pid_1_luid_0x00000000_0x0000BBBB_phys_0_eng_3_engtype_Copy", 3.25),
            ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_3_engtype_Copy", 0.0),
        ];

        double[] sums = SystemStatsService.GroupByAdapters(instances);

        Assert.Equal(2, sums.Length);
        Assert.Equal(4.0, sums[0], precision: 2);   // AAAA first (ordinal)
        Assert.Equal(23.25, sums[1], precision: 2); // BBBB second
    }

    [Fact]
    public void GroupByAdapters_IncludesFullyIdleAdapter_AsZero()
    {
        (string, double)[] instances =
        [
            ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 12.0),
            ("pid_1_luid_0x00000000_0x0000BBBB_phys_0_eng_0_engtype_3D", 0.0),
        ];

        double[] sums = SystemStatsService.GroupByAdapters(instances);

        Assert.Equal(2, sums.Length);
        Assert.Equal(0.0, sums[1], precision: 1);
    }

    [Fact]
    public void GroupByAdapters_ClampsEachAdapter_To100()
    {
        (string, double)[] instances =
        [
            ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 90.0),
            ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_3_engtype_Copy", 60.0),
        ];

        double[] sums = SystemStatsService.GroupByAdapters(instances);

        Assert.Single(sums);
        Assert.Equal(100.0, sums[0], precision: 1);
    }

    [Fact]
    public void GroupByAdapters_SkipsUnparsableNames()
    {
        (string, double)[] instances =
        [
            ("pid_123_phys_0_eng_0", 50.0),
            ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 7.0),
        ];

        double[] sums = SystemStatsService.GroupByAdapters(instances);

        Assert.Single(sums);
        Assert.Equal(7.0, sums[0], precision: 2);
    }

    [Fact]
    public void GroupByAdapters_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(SystemStatsService.GroupByAdapters(null));
        Assert.Empty(SystemStatsService.GroupByAdapters(Array.Empty<(string, double)>()));
    }

    // --- Software adapter exclusion (Microsoft Basic Render Driver / WARP) ---

    [Fact]
    public void GetSoftwareAdapterLuidKeys_ReturnsWellFormattedKeys()
    {
        // Live DXGI path: if intact it returns only keys of the form 0xHHHHHHHH_0xLLLLLLLL;
        // on this machine WARP (0x00000000_0x0001217E) is present, so the set is not empty.
        IReadOnlySet<string> keys = SystemStatsNative.GetSoftwareAdapterLuidKeys();

        Assert.All(keys, key =>
            Assert.Matches("^0x[0-9A-F]{8}_0x[0-9A-F]{8}$", key));
    }

    [Fact]
    public void GroupByAdapters_ExcludesSoftwareAdapterKeys()
    {
        // Proven on this machine: NVIDIA (0x10FF2) + AMD iGPU (0x121F4) are real,
        // 0x0001217E = Microsoft Basic Render Driver (software) — Task Manager
        // does not list it either. This was the source of the widget's 3rd "GPU" segment.
        (string, double)[] instances =
        [
            ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 4.0),
            ("pid_4_luid_0x00000000_0x0000CCCC_phys_0_eng_9_engtype_Video Decode 1", 3.7),
        ];
        var software = new HashSet<string> { "0x00000000_0x0000CCCC" };

        double[] sums = SystemStatsService.GroupByAdapters(instances, software);

        Assert.Single(sums);
        Assert.Equal(4.0, sums[0], precision: 2);
    }

    [Fact]
    public void GroupByAdapters_NullOrEmptyExclusionSet_KeepsAllAdapters()
    {
        (string, double)[] instances =
        [
            ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 4.0),
            ("pid_1_luid_0x00000000_0x0000CCCC_phys_0_eng_0_engtype_3D", 3.0),
        ];

        Assert.Equal(2, SystemStatsService.GroupByAdapters(instances, null).Length);
        Assert.Equal(2, SystemStatsService.GroupByAdapters(instances, new HashSet<string>()).Length);
    }

    // --- Top-bar text formatting ---

    [Fact]
    public void FormatText_CombinedMode_SumsAdaptersIntoOneSegment()
    {
        string text = SystemStatsService.FormatText(
            cpuPercent: 12.4, gpuPercents: [4.2, 23.5], ramPercent: 46.5,
            showGpuPerAdapter: false);
        Assert.Equal("CPU 12% · GPU 28% · RAM 47%", text);
    }

    [Fact]
    public void FormatText_PerAdapter_NumberedSegments()
    {
        string text = SystemStatsService.FormatText(
            cpuPercent: 12.4, gpuPercents: [4.2, 23.5], ramPercent: 46.5,
            showGpuPerAdapter: true);
        Assert.Equal("CPU 12% · GPU0 4% · GPU1 24% · RAM 47%", text);
    }

    [Fact]
    public void FormatText_PerAdapter_SingleGpu_Unnumbered()
    {
        string text = SystemStatsService.FormatText(
            cpuPercent: 12.4, gpuPercents: [7.6], ramPercent: 46.5,
            showGpuPerAdapter: true);
        Assert.Equal("CPU 12% · GPU 8% · RAM 47%", text);
    }

    [Fact]
    public void FormatText_OmitsMissingGpu()
    {
        string text = SystemStatsService.FormatText(
            cpuPercent: 12.4, gpuPercents: null, ramPercent: 46.5,
            showGpuPerAdapter: true);
        Assert.Equal("CPU 12% · RAM 47%", text);
    }

    [Fact]
    public void FormatText_NoSamplesYet_ShowsPlaceholder()
    {
        string text = SystemStatsService.FormatText(
            cpuPercent: null, gpuPercents: null, ramPercent: null,
            showGpuPerAdapter: true);
        Assert.Equal("--", text);
    }

    // --- Change detection key (mode-independent) ---

    [Fact]
    public void ChangeKey_IgnoresArrayIdentity()
    {
        var first = new SystemStatsSnapshot(12.4, [4.2, 23.5], 46.5);
        var second = new SystemStatsSnapshot(12.4, [4.2, 23.5], 46.5);

        Assert.Equal(first.ChangeKey, second.ChangeKey);
    }

    [Fact]
    public void ChangeKey_DetectsValueChanges()
    {
        var baseline = new SystemStatsSnapshot(12.4, [4.2, 23.5], 46.5);

        Assert.NotEqual(baseline.ChangeKey, (baseline with { CpuPercent = 13.0 }).ChangeKey);
        Assert.NotEqual(baseline.ChangeKey, (baseline with { GpuPercents = [4.2, 30.0] }).ChangeKey);
        Assert.NotEqual(baseline.ChangeKey, (baseline with { RamPercent = 40.0 }).ChangeKey);
    }

    // --- Native ABI layout contract (x64) ---

    [Fact]
    public void PdhCounterStructs_MatchNativeAbiLayout()
    {
        // Native x64: PDH_FMT_COUNTERVALUE = DWORD CStatus(4) + pad(4) + union(8) = 16 bytes;
        // PDH_FMT_COUNTERVALUE_ITEM_W = szName ptr(8) + value(16) = 24 bytes.
        // A smaller managed stride reads item[N].szName from garbage → AccessViolation.
        Type? value = typeof(GpuUsageQuery).GetNestedType(
            "PDH_FMT_COUNTERVALUE", BindingFlags.NonPublic | BindingFlags.Instance);
        Type? item = typeof(GpuUsageQuery).GetNestedType(
            "PDH_FMT_COUNTERVALUE_ITEM_W", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(value);
        Assert.NotNull(item);
        Assert.Equal(16, Marshal.SizeOf(value!));
        Assert.Equal(24, Marshal.SizeOf(item!));
    }

    // --- Instance name pointer guard ---

    [Fact]
    public void IsPointerInBuffer_AcceptsOnlyInBoundsNames()
    {
        IntPtr buffer = new(0x1_0000);
        uint size = 0x100;

        Assert.True(GpuUsageQuery.IsPointerInBuffer(new(0x1_0000), buffer, size));      // start
        Assert.True(GpuUsageQuery.IsPointerInBuffer(new(0x1_00FF), buffer, size));      // last byte
        Assert.False(GpuUsageQuery.IsPointerInBuffer(new(0x0_FFFF), buffer, size));     // before
        Assert.False(GpuUsageQuery.IsPointerInBuffer(new(0x1_0100), buffer, size));     // end
        Assert.False(GpuUsageQuery.IsPointerInBuffer(IntPtr.Zero, buffer, size));       // null
    }

    // --- Live PDH integration (exercises the real marshaling path) ---

    [Fact]
    public void GpuUsageQuery_RealSample_ReturnsNamedInstances()
    {
        GpuUsageQuery? query = GpuUsageQuery.TryCreate();
        if (query is null)
        {
            return; // No GPU engine counters on this machine (VM / driver).
        }

        try
        {
            // Utilization is an interval counter: first collect only primes.
            Assert.False(query.TrySample(out _));
            Thread.Sleep(120);
            Assert.True(query.TrySample(out (string Name, double Value)[] instances));

            // A wrong struct stride reads garbage name pointers here — historically an
            // AccessViolationException that killed the process.
            Assert.NotEmpty(instances);
            Assert.All(instances, instance => Assert.False(string.IsNullOrEmpty(instance.Name)));
            Assert.All(instances, instance => Assert.InRange(instance.Value, 0d, 100d));
        }
        finally
        {
            query.Dispose();
        }
    }

    [Fact]
    public void BytesPerSecond_RejectsResetAndZeroInterval()
    {
        Assert.Null(NetworkRate.BytesPerSecond(10, 5, 1));
        Assert.Null(NetworkRate.BytesPerSecond(0, 100, 0));
        Assert.Equal(50d, NetworkRate.BytesPerSecond(0, 100, 2));
    }

    [Fact]
    public void FormatBytesPerSecond_UsesUiCultureUnits()
    {
        Assert.Equal("—", NetworkRate.FormatBytesPerSecond(null));
        Assert.Equal("—", NetworkRate.FormatBytesPerSecond(-1));

        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            Assert.Equal("512 B/sn", NetworkRate.FormatBytesPerSecond(512));
            Assert.Equal("1,5 KB/sn", NetworkRate.FormatBytesPerSecond(1536));
            Assert.Equal("1,0 MB/sn", NetworkRate.FormatBytesPerSecond(1024d * 1024d));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            Assert.Equal("512 B/s", NetworkRate.FormatBytesPerSecond(512));
            Assert.Equal("1.5 KB/s", NetworkRate.FormatBytesPerSecond(1536));
            Assert.Equal("1.0 MB/s", NetworkRate.FormatBytesPerSecond(1024d * 1024d));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void Sparkline_Decimate_KeepsNewestPoints()
    {
        Assert.Empty(Sparkline.Decimate(null, 4));
        Assert.Equal(new[] { 1d, 2d }, Sparkline.Decimate([1d, 2d], 4));
        Assert.Equal(new[] { 3d, 4d }, Sparkline.Decimate([1d, 2d, 3d, 4d], 2));
        Assert.Empty(Sparkline.Decimate([1d], 0));
    }

    [Fact]
    public void NotificationDigest_CapsAtEight()
    {
        var rows = new NotificationText[10];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new NotificationText($"t{i}", $"b{i}");
        }

        IReadOnlyList<NotificationText> recent = NotificationDigest.TakeRecent(rows);
        Assert.Equal(NotificationDigest.MaxItems, recent.Count);
        Assert.Equal("t0", recent[0].Title);
        Assert.Equal("t7", recent[7].Title);
        Assert.Empty(NotificationDigest.TakeRecent(null));
    }

    [Fact]
    public void SameReadout_IgnoresSubPercentNoise()
    {
        var left = new SystemStatsSnapshot(12.2, [4.2], 40.2, NetworkDownText: "1,0 KB/sn", NetworkUpText: "—");
        var right = new SystemStatsSnapshot(12.4, [4.4], 40.4, NetworkDownText: "1,0 KB/sn", NetworkUpText: "—");
        Assert.True(SystemStatsService.SameReadout(left, right));
    }

    [Fact]
    public void SameReadout_NoticesARoundedStepOrNetworkLabel()
    {
        var baseline = new SystemStatsSnapshot(12.2, [4.2], 40.2, NetworkDownText: "1,0 KB/sn", NetworkUpText: "—");
        Assert.False(SystemStatsService.SameReadout(
            baseline,
            baseline with { CpuPercent = 12.6 }));
        Assert.False(SystemStatsService.SameReadout(
            baseline,
            baseline with { NetworkDownText = "2,0 KB/sn" }));
    }
}
