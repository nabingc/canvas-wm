namespace CanvasWm.Engine.Routing;

/// <summary>Router timings. Immutable; with-expressions. Every value finite and greater than 0,
/// else ArgumentOutOfRangeException from the init accessor.</summary>
public sealed record RouterOptions
{
    private readonly double _displaySetChangeDelaySeconds = 1.5;
    private readonly double _workAreaRefreshDelaySeconds = 0.4;
    private readonly double _reshowDelaySeconds = 0.8;
    private readonly double _shutdownTimeoutSeconds = 2.0;

    /// <summary>The default options; the same instance every time.</summary>
    public static RouterOptions Default { get; } = new();

    /// <summary>Delay before a changed display set is rebuilt, s. Default 1.5.</summary>
    public double DisplaySetChangeDelaySeconds
    {
        get => _displaySetChangeDelaySeconds;
        init => _displaySetChangeDelaySeconds = Check(value, nameof(DisplaySetChangeDelaySeconds));
    }

    /// <summary>Delay before work areas are refreshed when the set is unchanged, s. Default 0.4.</summary>
    public double WorkAreaRefreshDelaySeconds
    {
        get => _workAreaRefreshDelaySeconds;
        init => _workAreaRefreshDelaySeconds = Check(value, nameof(WorkAreaRefreshDelaySeconds));
    }

    /// <summary>Delay between a rebuild and re-showing desktops, s. Default 0.8.</summary>
    public double ReshowDelaySeconds
    {
        get => _reshowDelaySeconds;
        init => _reshowDelaySeconds = Check(value, nameof(ReshowDelaySeconds));
    }

    /// <summary>Longest wait for release completions when quitting, s. Default 2.0.</summary>
    public double ShutdownTimeoutSeconds
    {
        get => _shutdownTimeoutSeconds;
        init => _shutdownTimeoutSeconds = Check(value, nameof(ShutdownTimeoutSeconds));
    }

    private static double Check(double value, string name) =>
        double.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "Must be finite and greater than 0.");
}
