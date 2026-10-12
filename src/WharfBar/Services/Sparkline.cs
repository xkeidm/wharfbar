using System.Windows;
using System.Windows.Media;

namespace WharfBar.Services;

/// <summary>Pure helpers for the stats hover line. No chart package.</summary>
internal static class Sparkline
{
    internal const int DefaultPoints = 24;

    /// <summary>Keeps the newest samples, at most <paramref name="maxPoints"/>.</summary>
    internal static IReadOnlyList<double> Decimate(IReadOnlyList<double>? samples, int maxPoints)
    {
        if (samples is null || samples.Count == 0 || maxPoints <= 0)
        {
            return [];
        }

        if (samples.Count <= maxPoints)
        {
            var copy = new double[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                copy[i] = samples[i];
            }

            return copy;
        }

        var tail = new double[maxPoints];
        int start = samples.Count - maxPoints;
        for (int i = 0; i < maxPoints; i++)
        {
            tail[i] = samples[start + i];
        }

        return tail;
    }

    /// <summary>
    /// Maps samples into a polyline. A flat series sits on the midline.
    /// Y grows upward in the data and downward in WPF, so the high value is near y = 0.
    /// </summary>
    internal static PointCollection Plot(IReadOnlyList<double>? samples, double width, double height)
    {
        var points = new PointCollection();
        if (samples is null || samples.Count == 0 || width <= 0 || height <= 0)
        {
            return points;
        }

        double min = samples[0];
        double max = samples[0];
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i] < min)
            {
                min = samples[i];
            }

            if (samples[i] > max)
            {
                max = samples[i];
            }
        }

        double span = max - min;
        double usable = Math.Max(1, height - 1);
        for (int i = 0; i < samples.Count; i++)
        {
            double x = samples.Count == 1 ? width / 2d : width * i / (samples.Count - 1);
            double y = span <= 0.0001d
                ? height / 2d
                : usable - ((samples[i] - min) / span * usable);
            points.Add(new Point(x, y));
        }

        return points;
    }
}
