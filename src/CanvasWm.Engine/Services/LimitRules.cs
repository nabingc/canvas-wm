using CanvasWm.Core.Geometry;

namespace CanvasWm.Engine.Services;

/// <summary>The learned-minimum update rules. Pure.</summary>
public static class LimitRules
{
    /// <summary>Observe lowers the stored minimum only when the observed size is smaller by
    /// strictly more than this on an axis.</summary>
    public const double ObserveSlack = 2.0;

    /// <summary>New stored value after learning <paramref name="minimum"/>.</summary>
    /// <param name="stored">Currently stored value, or null.</param>
    /// <param name="minimum">The learned minimum.</param>
    /// <returns>The new stored value.</returns>
    public static Size Learn(Size? stored, Size minimum)
    {
        Size b = stored ?? new Size(0, 0);
        // SPEC-ASSUMPTION A13: non-finite inputs are ignored.
        if (!double.IsFinite(minimum.Width) || !double.IsFinite(minimum.Height)) return b;
        return new Size(Math.Max(b.Width, minimum.Width), Math.Max(b.Height, minimum.Height));
    }

    /// <summary>New stored value after observing <paramref name="observed"/>; null when nothing is stored.</summary>
    /// <param name="stored">Currently stored value, or null.</param>
    /// <param name="observed">The observed size.</param>
    /// <returns>The new stored value.</returns>
    public static Size? Observe(Size? stored, Size observed)
    {
        if (stored is not { } s) return null;
        // SPEC-ASSUMPTION A13: non-finite inputs are ignored.
        if (!double.IsFinite(observed.Width) || !double.IsFinite(observed.Height)) return s;
        if (observed.Width < s.Width - ObserveSlack || observed.Height < s.Height - ObserveSlack)
        {
            return new Size(Math.Min(s.Width, observed.Width), Math.Min(s.Height, observed.Height));
        }

        return s;
    }
}
