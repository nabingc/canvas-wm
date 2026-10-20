using CanvasWm.Core.Layout;

namespace CanvasWm.Engine.Tests.WindowEventsSpec;

/// <summary>Deterministic id generator: "n1", "n2", ... and ignores the prefix.</summary>
internal sealed class SequentialIds : IIdGenerator
{
    private int _n;

    public string Make(string prefix) => "n" + (++_n);
}
