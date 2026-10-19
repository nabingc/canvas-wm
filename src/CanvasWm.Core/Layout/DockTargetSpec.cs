namespace CanvasWm.Core.Layout;

/// <summary>Where to dock: beside node Id on Edge, or at Seam when Seam is non-null
/// (Id and Edge are then ignored by dock).</summary>
/// <param name="Id">Target node id (any kind). Non-null.</param>
/// <param name="Edge">Edge of the target to dock on.</param>
/// <param name="Seam">Optional seam; when set it takes precedence.</param>
public sealed record DockTargetSpec(string Id, Edge Edge, SplitSeam? Seam = null)
{
    /// <summary>Target node id.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public string Id { get; init; } = Id ?? throw new ArgumentNullException(nameof(Id));
}
