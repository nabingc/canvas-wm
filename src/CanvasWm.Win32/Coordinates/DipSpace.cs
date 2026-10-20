using CanvasWm.Core.Geometry;

namespace CanvasWm.Win32.Coordinates;

/// <summary>The single mapping between physical pixels and CanvasWM DIPs (B10). Pure.
/// Every Win32 feature (#10 facts, #11 frame writes, #29 display frames) must use it.</summary>
public static class DipSpace
{
    /// <summary>96: the DPI at which one physical pixel equals one DIP.</summary>
    public const double BaseDpi = 96.0;

    /// <summary>dpi / 96.</summary>
    /// <param name="dpi">The monitor's effective DPI.</param>
    /// <returns>The scale factor.</returns>
    /// <exception cref="ArgumentOutOfRangeException">dpi is 0.</exception>
    public static double ScaleFor(uint dpi)
    {
        if (dpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "dpi must be non-zero.");
        }

        return dpi / BaseDpi;
    }

    /// <summary>Maps a physical rectangle to DIPs relative to the monitor it belongs to:
    /// X = M.Left + (r.Left - M.Left)/s, Y = M.Top + (r.Top - M.Top)/s, Width = r.Width/s,
    /// Height = r.Height/s, where M = monitorBounds and s = ScaleFor(monitorDpi). The monitor's
    /// own DIP frame is ToDip(M, M, dpi) = (M.Left, M.Top, M.Width/s, M.Height/s).</summary>
    /// <param name="pixels">The rectangle in physical pixels.</param>
    /// <param name="monitorBounds">The monitor's physical bounds.</param>
    /// <param name="monitorDpi">The monitor's effective DPI.</param>
    /// <returns>The rectangle in DIPs.</returns>
    /// <exception cref="ArgumentOutOfRangeException">monitorDpi is 0.</exception>
    public static Rect ToDip(PixelRect pixels, PixelRect monitorBounds, uint monitorDpi)
    {
        double s = ScaleFor(monitorDpi);
        return new Rect(
            monitorBounds.Left + ((double)pixels.Left - monitorBounds.Left) / s,
            monitorBounds.Top + ((double)pixels.Top - monitorBounds.Top) / s,
            (double)pixels.Width / s,
            (double)pixels.Height / s);
    }
}
