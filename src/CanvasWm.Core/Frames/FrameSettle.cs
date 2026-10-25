using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Frames;

/// <summary>The fixed in-write correction rules a window-system adapter may apply (amended
/// engine-interfaces B7). Pure.</summary>
public static class FrameSettle
{
    /// <summary>A read-back that differs from the target by strictly more than this on any of
    /// X, Y, Width, Height triggers the one settle pass: 1.0 DIP.</summary>
    public const double Tolerance = 1.0;

    /// <summary>A write is an off-screen write when the target's visible fraction is strictly
    /// below this: 0.05.</summary>
    public const double OffScreenWriteBelow = 0.05;

    /// <summary>An off-screen write is re-pushed once when the result's visible fraction is
    /// strictly above this: 0.02.</summary>
    public const double RepushAbove = 0.02;

    /// <summary>True when |actual.c - target.c| &gt; Tolerance for some c in {X, Y, Width,
    /// Height}. A NaN difference counts as exceeding (true).</summary>
    /// <param name="target">The requested frame.</param>
    /// <param name="actual">The frame read back.</param>
    /// <returns>Whether the settle pass is needed.</returns>
    public static bool NeedsSecondPass(Rect target, Rect actual) =>
        Exceeds(actual.X - target.X) || Exceeds(actual.Y - target.Y)
        || Exceeds(actual.Width - target.Width) || Exceeds(actual.Height - target.Height);

    /// <summary>targetVisibleFraction &lt; OffScreenWriteBelow. NaN gives false.</summary>
    /// <param name="targetVisibleFraction">The target's visible fraction.</param>
    /// <returns>Whether the write is an off-screen write.</returns>
    public static bool IsOffScreenWrite(double targetVisibleFraction) => targetVisibleFraction < OffScreenWriteBelow;

    /// <summary>offScreenWrite &amp;&amp; resultVisibleFraction &gt; RepushAbove. NaN gives
    /// false.</summary>
    /// <param name="offScreenWrite">Whether the write was an off-screen write.</param>
    /// <param name="resultVisibleFraction">The result's visible fraction.</param>
    /// <returns>Whether to re-push the position.</returns>
    public static bool NeedsRepush(bool offScreenWrite, double resultVisibleFraction) =>
        offScreenWrite && resultVisibleFraction > RepushAbove;

    private static bool Exceeds(double difference) => !(Math.Abs(difference) <= Tolerance);
}
