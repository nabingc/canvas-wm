using System.Collections.Immutable;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Engine;

/// <summary>One expected-frame entry.</summary>
/// <param name="Frame">The expected frame.</param>
/// <param name="Time">Clock time the entry was recorded.</param>
internal readonly record struct ExpectedFrame(Rect Frame, double Time);

/// <summary>Snapshot of one managed window's writer state (diagnostics and tests).</summary>
/// <param name="Requested">Target of the most recent write, or null.</param>
/// <param name="WritesInFlight">Writes issued and not yet completed.</param>
/// <param name="PendingRechecks">Refusal rechecks scheduled and not yet finished (diagnostic, D3).</param>
/// <param name="Expected">Expected-frame list as stored (not pruned by reading the snapshot).</param>
/// <param name="ShownSize">Size of the last tile target written, or null.</param>
/// <param name="Minimum">Own learned minimum, or null. An axis may be 0.</param>
/// <param name="Maximum">Own maximum, or null. An unlimited axis is +∞.</param>
/// <param name="Natural">Natural size, or null.</param>
internal sealed record FrameWriterState(Rect? Requested, int WritesInFlight, int PendingRechecks,
    ImmutableArray<ExpectedFrame> Expected, Size? ShownSize, Size? Minimum, Size? Maximum, Size? Natural);

/// <summary>Mutable writer state of one managed record. Identity (reference equality) is the
/// "same record" test of the writer.</summary>
internal sealed class WriterRecord
{
    public Rect? Requested { get; set; }
    public int WritesInFlight { get; set; }
    public int PendingRechecks { get; set; }
    public List<ExpectedFrame> Expected { get; } = new();
    public Size? ShownSize { get; set; }
    public Size? Minimum { get; set; }
    public Size? Maximum { get; set; }
    public Size? Natural { get; set; }
    public double LastFocused { get; set; } = double.NegativeInfinity;
    public List<double> SnapBackTimes { get; } = new();

    public FrameWriterState Snapshot() =>
        new(Requested, WritesInFlight, PendingRechecks, Expected.ToImmutableArray(),
            ShownSize, Minimum, Maximum, Natural);
}
