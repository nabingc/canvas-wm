using CanvasWm.Core.Geometry;
using static CanvasWm.Engine.Tests.FrameWriterSpec.Wr;

namespace CanvasWm.Engine.Tests.FrameWriterSpec;

/// <summary>Amended B8.3 (win32-frame-writer R2): a known frame within SkipTolerance of the target is skipped.</summary>
public sealed class SkipToleranceR2Tests
{
    private static Wr Rig(Rect bKnown) =>
        new(L3(), null, new[] { ("a", A3), ("b", bKnown), ("c", C3) });

    [Fact]
    public void B8_3_known_422_6_vs_target_423_is_skipped()
    {
        var w = Rig(R(725, 35, 705, 422.6));
        w.E.PresentDirectly();
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void B8_3_known_421_9_vs_target_423_is_written()
    {
        var w = Rig(R(725, 35, 705, 421.9));
        w.E.PresentDirectly();
        var wr = w.Writes();
        Assert.Single(wr);
        Assert.Equal(K("b"), wr[0].Key);
        Assert.Equal(R(725, 35, 705, 423), wr[0].Target);
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(1.0001, false)]
    public void B8_3_boundary_on_X(double dx, bool skipped)
    {
        var w = Rig(R(725 + dx, 35, 705, 423));
        w.E.PresentDirectly();
        Assert.Equal(skipped, w.Writes().Count == 0);
    }

    [Fact]
    public void SkipTolerance_equals_one_dip()
    {
        Assert.Equal(1.0, CanvasWm.Engine.FrameWriting.SkipTolerance);
    }
}
