using CanvasWm.Core.Frames;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Parking;

namespace CanvasWm.Core.Tests.FramesSpec;

public sealed class FrameSettleTests
{
    [Fact]
    public void AC1_constants()
    {
        Assert.Equal(1.0, FrameSettle.Tolerance);
        Assert.Equal(0.05, FrameSettle.OffScreenWriteBelow);
        Assert.Equal(0.02, FrameSettle.RepushAbove);
    }

    [Fact]
    public void AC1_needs_second_pass_cases()
    {
        Assert.True(FrameSettle.NeedsSecondPass(new Rect(0, 0, 400, 600), new Rect(0, 0, 500, 600)));
        Assert.False(FrameSettle.NeedsSecondPass(new Rect(300, 25, 600, 400), new Rect(300.5, 25, 600, 400)));
        Assert.False(FrameSettle.NeedsSecondPass(new Rect(10, 10, 100, 100), new Rect(11.0, 10, 100, 100)));
        Assert.True(FrameSettle.NeedsSecondPass(new Rect(10, 10, 100, 100), new Rect(10, 10, 100, 101.0 + 1e-9)));
        Assert.False(FrameSettle.NeedsSecondPass(new Rect(5, 6, 7, 8), new Rect(5, 6, 7, 8)));
        Assert.True(FrameSettle.NeedsSecondPass(new Rect(0, 0, 100, 100), new Rect(0, 0, double.NaN, 100)));
        Assert.True(FrameSettle.NeedsSecondPass(new Rect(0, 10, 100, 100), new Rect(0, 8.5, 100, 100)));
    }

    [Fact]
    public void AC2_is_off_screen_write()
    {
        Assert.True(FrameSettle.IsOffScreenWrite(0.000003));
        Assert.True(FrameSettle.IsOffScreenWrite(0.0499));
        Assert.False(FrameSettle.IsOffScreenWrite(0.05));
        Assert.False(FrameSettle.IsOffScreenWrite(1));
        Assert.False(FrameSettle.IsOffScreenWrite(double.NaN));
    }

    [Fact]
    public void AC2_needs_repush()
    {
        Assert.True(FrameSettle.NeedsRepush(true, 0.059));
        Assert.False(FrameSettle.NeedsRepush(true, 0.02));
        Assert.True(FrameSettle.NeedsRepush(true, 0.0201));
        Assert.False(FrameSettle.NeedsRepush(false, 0.9));
        Assert.False(FrameSettle.NeedsRepush(true, double.NaN));
    }

    [Fact]
    public void AC2_worked_check()
    {
        var screens = new[] { new Rect(0, 0, 1440, 900) };
        double f = ParkGeometry.VisibleFraction(new Rect(1439, 899, 705, 423), screens);
        Assert.InRange(f, 1.0 / 298215 - 1e-12, 1.0 / 298215 + 1e-12);
        bool off = FrameSettle.IsOffScreenWrite(f);
        Assert.True(off);

        double f2 = ParkGeometry.VisibleFraction(new Rect(1000, 860, 705, 423), screens);
        Assert.InRange(f2, 17600.0 / 298215 - 1e-12, 17600.0 / 298215 + 1e-12);
        Assert.True(FrameSettle.NeedsRepush(off, f2));

        double f3 = ParkGeometry.VisibleFraction(new Rect(1439, 860, 705, 423), screens);
        Assert.InRange(f3, 40.0 / 298215 - 1e-12, 40.0 / 298215 + 1e-12);
        Assert.False(FrameSettle.NeedsRepush(off, f3));
    }
}
