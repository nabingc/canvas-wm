namespace CanvasWm.Core.Parking;

/// <summary>Tunable constants of park geometry. The defaults are the values observed in the
/// studied app on macOS. Feature #14 may choose different values for Windows.
/// Compiler-generated value equality.</summary>
public sealed record ParkOptions
{
    private readonly double _edgeOverlap = 1.0;
    private readonly double _reach = 60.0;
    private readonly double _tolerance = 1.0;
    private readonly double _visibleFractionThreshold = 0.05;
    private readonly double _smallDivisor = 3.0;

    /// <summary>Default options: EdgeOverlap 1, Reach 60, Tolerance 1,
    /// VisibleFractionThreshold 0.05, SmallDivisor 3.</summary>
    public static ParkOptions Default { get; } = new();

    /// <summary>How far a park spot overlaps its own display: the corner square side, the
    /// column width or the row height. Default 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is negative, NaN or infinite.</exception>
    public double EdgeOverlap
    {
        get => _edgeOverlap;
        init
        {
            // SPEC-ASSUMPTION A9: option validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(EdgeOverlap), value, "Must be finite and >= 0.");
            _edgeOverlap = value;
        }
    }

    /// <summary>Vertical extension of a candidate's reach rect, used for scoring and as the
    /// upward slack in RestsParked. Default 60.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is negative, NaN or infinite.</exception>
    public double Reach
    {
        get => _reach;
        init
        {
            // SPEC-ASSUMPTION A9: option validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(Reach), value, "Must be finite and >= 0.");
            _reach = value;
        }
    }

    /// <summary>Size and position tolerance of RestsParked. Default 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is not greater than 0, or is NaN or
    /// infinite.</exception>
    public double Tolerance
    {
        get => _tolerance;
        init
        {
            // SPEC-ASSUMPTION A9: option validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(Tolerance), value, "Must be finite and > 0.");
            _tolerance = value;
        }
    }

    /// <summary>A rect whose visible fraction is strictly below this value counts as off screen.
    /// Default 0.05.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is outside [0, 1] or is NaN.</exception>
    public double VisibleFractionThreshold
    {
        get => _visibleFractionThreshold;
        init
        {
            // SPEC-ASSUMPTION A9: option validation ranges are CanvasWM choices.
            if (!(value >= 0 && value <= 1)) throw new ArgumentOutOfRangeException(nameof(VisibleFractionThreshold), value, "Must be in [0, 1].");
            _visibleFractionThreshold = value;
        }
    }

    /// <summary>Park-small fits the window into (work-area width / SmallDivisor,
    /// work-area height / SmallDivisor). Default 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is less than 1, or is NaN or
    /// infinite.</exception>
    public double SmallDivisor
    {
        get => _smallDivisor;
        init
        {
            // SPEC-ASSUMPTION A9: option validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || value < 1) throw new ArgumentOutOfRangeException(nameof(SmallDivisor), value, "Must be finite and >= 1.");
            _smallDivisor = value;
        }
    }
}
