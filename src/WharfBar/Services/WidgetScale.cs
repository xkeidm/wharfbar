using WharfBar.Native;

namespace WharfBar.Services;

internal static class WidgetScale
{
    internal static double ToDip(int physical, uint dpi)
    {
        if (dpi == 0)
        {
            dpi = 96;
        }

        return physical * 96d / dpi;
    }

    internal static int ToPhysical(double dip, uint dpi)
    {
        if (dpi == 0)
        {
            dpi = 96;
        }

        return (int)Math.Round(dip * dpi / 96d);
    }

    internal static DipRect WorkToDip(RECT physical, uint dpi) =>
        new(
            ToDip(physical.Left, dpi),
            ToDip(physical.Top, dpi),
            ToDip(physical.Width, dpi),
            ToDip(physical.Height, dpi));
}
