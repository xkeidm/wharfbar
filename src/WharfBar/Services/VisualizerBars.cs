namespace WharfBar.Services;

/// <summary>
/// Five bar levels from the default-render peak. Attack eases toward the peak
/// (fast, slightly staggered, so hits land without a hard jump); each bar releases
/// at a different rate so a steady song still moves the chip. Silence decays to a
/// flat line — levels are not invented.
/// </summary>
internal static class VisualizerBars
{
    internal const int MaxDip = 14;
    internal const int Count = 5;

    // Relative drive per bar (presentation only; not a spectrum).
    private static readonly float[] Weights = [0.55f, 0.85f, 1f, 0.75f, 0.4f];

    // Per-tick attack fraction. Centre reacts first, edges trail a touch.
    private static readonly float[] Attack = [0.8f, 0.9f, 0.95f, 0.85f, 0.75f];

    // Per-tick release. Staggered so bars do not fall as one block.
    private static readonly float[] Decay = [0.78f, 0.66f, 0.52f, 0.7f, 0.84f];

    private static readonly float[] Levels = new float[Count];

    internal static void Reset()
    {
        Array.Clear(Levels);
    }

    /// <summary>Advances one tick and returns the new levels (0..1, a fresh array).</summary>
    internal static float[] Step(float peak)
    {
        // Shared-mode peaks are often 0.05–0.35; a soft curve lifts them into
        // the visible range without inventing motion when peak is zero.
        float drive = MathF.Pow(Math.Clamp(peak, 0f, 1f), 0.35f);
        var result = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            float target = drive * Weights[i];
            float level = Levels[i];
            level = target >= level
                ? level + (target - level) * Attack[i]
                : level * Decay[i];
            if (level < 0.004f)
            {
                level = 0f;
            }

            Levels[i] = level;
            result[i] = level;
        }

        return result;
    }

    /// <summary>Same step as <see cref="Step"/>, as whole DIP heights (at least 1).</summary>
    internal static int[] Heights(float peak)
    {
        float[] levels = Step(peak);
        var heights = new int[Count];
        for (int i = 0; i < Count; i++)
        {
            heights[i] = Math.Max(1, (int)Math.Round(levels[i] * MaxDip));
        }

        return heights;
    }
}
