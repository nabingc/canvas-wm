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

    /// <summary>Inverse of ToDip for one monitor. With M = monitorBounds and s = ScaleFor(dpi):
    /// Left = R(M.Left + (d.X - M.Left)*s), Top = R(M.Top + (d.Y - M.Top)*s),
    /// Right = R(M.Left + (d.X + d.Width - M.Left)*s), Bottom = R(M.Top + (d.Y + d.Height - M.Top)*s),
    /// where R rounds half away from zero and then saturates to the int range.
    /// Edges are rounded, not sizes, so rects that share an edge in DIPs share it in pixels.</summary>
    /// <param name="dips">The rectangle in DIPs.</param>
    /// <param name="monitorBounds">The monitor's physical bounds.</param>
    /// <param name="monitorDpi">The monitor's effective DPI.</param>
    /// <returns>The rectangle in physical pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException">monitorDpi is 0, or a component of dips
    /// is NaN or infinite.</exception>
    public static PixelRect ToPixels(Rect dips, PixelRect monitorBounds, uint monitorDpi)
    {
        double s = ScaleFor(monitorDpi);
        if (!double.IsFinite(dips.X) || !double.IsFinite(dips.Y)
            || !double.IsFinite(dips.Width) || !double.IsFinite(dips.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(dips), "rect components must be finite.");
        }

        double ml = monitorBounds.Left;
        double mt = monitorBounds.Top;
        return new PixelRect(
            Round(ml + (dips.X - ml) * s),
            Round(mt + (dips.Y - mt) * s),
            Round(ml + (dips.X + dips.Width - ml) * s),
            Round(mt + (dips.Y + dips.Height - mt) * s));
    }

    /// <summary>Point form of ToDip: (M.Left + (x - M.Left)/s, M.Top + (y - M.Top)/s).</summary>
    /// <param name="x">Physical x.</param>
    /// <param name="y">Physical y.</param>
    /// <param name="monitorBounds">The monitor's physical bounds.</param>
    /// <param name="monitorDpi">The monitor's effective DPI.</param>
    /// <returns>The point in DIPs.</returns>
    /// <exception cref="ArgumentOutOfRangeException">monitorDpi is 0.</exception>
    public static Point ToDip(int x, int y, PixelRect monitorBounds, uint monitorDpi)
    {
        double s = ScaleFor(monitorDpi);
        return new Point(
            monitorBounds.Left + ((double)x - monitorBounds.Left) / s,
            monitorBounds.Top + ((double)y - monitorBounds.Top) / s);
    }

    private static int Round(double v)
    {
        double r = Math.Round(v, MidpointRounding.AwayFromZero);
        if (r >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return r <= int.MinValue ? int.MinValue : (int)r;
    }
}
