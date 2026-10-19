namespace CanvasWm.Core.Layout;

/// <summary>Inputs for minimum-aware solving.</summary>
public sealed record LayoutMetrics
{
    /// <summary>Creates metrics.</summary>
    /// <param name="width">Width, in DIPs, that the unit square stands for. Finite, at least 0.</param>
    /// <param name="height">Height, in DIPs, that the unit square stands for. Finite, at least 0.</param>
    /// <param name="min">Minimum extent, in DIPs, of a node along an axis.</param>
    /// <exception cref="ArgumentOutOfRangeException">width or height is NaN, infinite or negative.</exception>
    /// <exception cref="ArgumentNullException">min is null.</exception>
    public LayoutMetrics(double width, double height, Func<LayoutNode, Axis, double> min)
    {
        if (!double.IsFinite(width) || width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be finite and >= 0.");
        }
        if (!double.IsFinite(height) || height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be finite and >= 0.");
        }
        ArgumentNullException.ThrowIfNull(min);
        Width = width;
        Height = height;
        Min = min;
    }

    /// <summary>Width, in DIPs, that the unit square stands for.</summary>
    public double Width { get; }

    /// <summary>Height, in DIPs, that the unit square stands for.</summary>
    public double Height { get; }

    /// <summary>Minimum extent, in DIPs, of a node along an axis (feature #17 supplies the real provider).</summary>
    public Func<LayoutNode, Axis, double> Min { get; }
}
