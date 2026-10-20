using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Projection;

/// <summary>What is known about one managed window. All members optional.</summary>
public sealed record WindowFacts
{
    /// <summary>Facts with every member null.</summary>
    public static WindowFacts None { get; } = new();

    /// <summary>Size of the window's last known frame (park size).</summary>
    public Size? LastFrameSize { get; init; }

    /// <summary>The window's own (learned) minimum size.</summary>
    public Size? Minimum { get; init; }

    /// <summary>The window's maximum size.</summary>
    public Size? Maximum { get; init; }

    /// <summary>Natural size; used only for the aspect fit of floating frames.</summary>
    public Size? Natural { get; init; }
}
