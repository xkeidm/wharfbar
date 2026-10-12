namespace WharfBar.Services;

/// <summary>
/// Five bar heights from one measured peak. The shape is fixed so the bars
/// are not a made-up waveform; a zero peak is a flat line.
/// </summary>
internal static class VisualizerBars
{
    internal static int[] Heights(float peak)
    {
        int level = (int)Math.Round(Math.Clamp(peak, 0f, 1f) * 14f);
        float[] weights = [0.45f, 0.8f, 1f, 0.7f, 0.35f];
        var heights = new int[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            heights[i] = Math.Max(1, (int)Math.Round(level * weights[i]));
        }

        return heights;
    }
}
