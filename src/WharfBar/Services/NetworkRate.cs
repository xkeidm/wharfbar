using System.Globalization;
using System.Net.NetworkInformation;

namespace WharfBar.Services;

/// <summary>
/// Throughput from <see cref="NetworkInterface"/> counters. Formatting is pure;
/// the live sum runs on the existing system-stats interval.
/// </summary>
internal static class NetworkRate
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Bytes per second between two counter samples. A reset or a non-positive interval is null.</summary>
    internal static double? BytesPerSecond(long previous, long current, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0 || current < previous)
        {
            return null;
        }

        return (current - previous) / elapsedSeconds;
    }

    /// <summary>
    /// Compact rate. Null, NaN, infinity, and negatives become an em dash.
    /// Uses tr-TR so the decimal comma matches the rest of the bar.
    /// </summary>
    internal static string FormatBytesPerSecond(double? bytesPerSecond)
    {
        if (bytesPerSecond is not double value
            || double.IsNaN(value)
            || double.IsInfinity(value)
            || value < 0)
        {
            return "—";
        }

        const double kilo = 1024d;
        if (value < kilo)
        {
            return string.Create(Turkish, $"{value:0} B/sn");
        }

        if (value < kilo * kilo)
        {
            return string.Create(Turkish, $"{value / kilo:0.0} KB/sn");
        }

        return string.Create(Turkish, $"{value / (kilo * kilo):0.0} MB/sn");
    }

    /// <summary>Sum of up, non-loopback interface counters. A failed adapter is skipped.</summary>
    internal static (long Received, long Sent) SumUpCounters()
    {
        long received = 0;
        long sent = 0;
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                try
                {
                    IPInterfaceStatistics stats = nic.GetIPStatistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
                catch (NetworkInformationException)
                {
                }
            }
        }
        catch (NetworkInformationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"WharfBar: network counters failed — {ex.Message}");
        }

        return (received, sent);
    }
}
