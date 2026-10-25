namespace CanvasWm.Core.Frames;

/// <summary>Widths of a window's invisible border on each side, in device pixels (opaque
/// integers to Core). Never negative when produced by Measure or Rescale.</summary>
/// <param name="Left">Left inset.</param>
/// <param name="Top">Top inset.</param>
/// <param name="Right">Right inset.</param>
/// <param name="Bottom">Bottom inset.</param>
public readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
{
    /// <summary>(0, 0, 0, 0).</summary>
    public static FrameInsets Zero { get; } = new(0, 0, 0, 0);

    /// <summary>Insets between an outer rect and the visible rect inside it (edges as
    /// left/top inclusive, right/bottom exclusive): Left = visibleLeft - outerLeft,
    /// Top = visibleTop - outerTop, Right = outerRight - visibleRight,
    /// Bottom = outerBottom - visibleBottom. Each side is computed in 64-bit and clamped to
    /// [0, int.MaxValue].</summary>
    /// <param name="outerLeft">Outer left.</param>
    /// <param name="outerTop">Outer top.</param>
    /// <param name="outerRight">Outer right.</param>
    /// <param name="outerBottom">Outer bottom.</param>
    /// <param name="visibleLeft">Visible left.</param>
    /// <param name="visibleTop">Visible top.</param>
    /// <param name="visibleRight">Visible right.</param>
    /// <param name="visibleBottom">Visible bottom.</param>
    /// <returns>The insets.</returns>
    public static FrameInsets Measure(int outerLeft, int outerTop, int outerRight, int outerBottom,
                                      int visibleLeft, int visibleTop, int visibleRight, int visibleBottom) =>
        new(Side((long)visibleLeft - outerLeft),
            Side((long)visibleTop - outerTop),
            Side((long)outerRight - visibleRight),
            Side((long)outerBottom - visibleBottom));

    /// <summary>Each side times toDpi / fromDpi, rounded half away from zero. Returns this
    /// unchanged when fromDpi == toDpi.</summary>
    /// <param name="fromDpi">The DPI the insets were measured at.</param>
    /// <param name="toDpi">The DPI to convert to.</param>
    /// <returns>The rescaled insets.</returns>
    /// <exception cref="ArgumentOutOfRangeException">fromDpi or toDpi is 0.</exception>
    public FrameInsets Rescale(uint fromDpi, uint toDpi)
    {
        if (fromDpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fromDpi), "dpi must be non-zero.");
        }

        if (toDpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toDpi), "dpi must be non-zero.");
        }

        if (fromDpi == toDpi)
        {
            return this;
        }

        return new FrameInsets(
            Scale(Left, fromDpi, toDpi), Scale(Top, fromDpi, toDpi),
            Scale(Right, fromDpi, toDpi), Scale(Bottom, fromDpi, toDpi));
    }

    private static int Side(long value) => (int)Math.Clamp(value, 0L, int.MaxValue);

    private static int Scale(int value, uint fromDpi, uint toDpi)
    {
        decimal scaled = Math.Round((decimal)value * toDpi / fromDpi, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(scaled, int.MinValue, int.MaxValue);
    }
}
