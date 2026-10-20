namespace CanvasWm.Engine.Tracking;

/// <summary>Tracker timings and thresholds. Immutable; with-expressions.</summary>
public sealed record TrackerOptions
{
    private double _first = 0.05;
    private double _second = 0.4;
    private double _rescan = 3.0;
    private double _offScreen = 0.05;
    private string _fallback = "App";

    /// <summary>The default options. The same instance every time.</summary>
    public static TrackerOptions Default { get; } = new();

    /// <summary>First delayed re-check after a may-have-appeared event, s. Default 0.05.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not finite or not above 0.</exception>
    public double FirstRecheckSeconds
    {
        get => _first;
        init => _first = Positive(value, nameof(FirstRecheckSeconds));
    }

    /// <summary>Second delayed re-check, s. Default 0.4.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not finite or not above 0.</exception>
    public double SecondRecheckSeconds
    {
        get => _second;
        init => _second = Positive(value, nameof(SecondRecheckSeconds));
    }

    /// <summary>Period of the full safety rescan, s. Default 3.0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not finite or not above 0.</exception>
    public double RescanSeconds
    {
        get => _rescan;
        init => _rescan = Positive(value, nameof(RescanSeconds));
    }

    /// <summary>A window whose centre is on no display and that no engine holds is admitted only
    /// when its visible fraction is strictly below this. Default 0.05.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Outside [0, 1] or NaN.</exception>
    public double OffScreenAdmitBelow
    {
        get => _offScreen;
        init
        {
            if (double.IsNaN(value) || value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(OffScreenAdmitBelow), value, "Must be in [0, 1].");
            }

            _offScreen = value;
        }
    }

    /// <summary>AppName used when the facts carry none. Default "App".</summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    /// <exception cref="ArgumentException">Set to empty.</exception>
    public string FallbackAppName
    {
        get => _fallback;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value, nameof(FallbackAppName));
            _fallback = value;
        }
    }

    private static double Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Must be finite and greater than 0.");
        }

        return value;
    }
}
