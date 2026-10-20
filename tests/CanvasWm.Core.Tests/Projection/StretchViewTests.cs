using System.Collections.Immutable;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Projection;
using static CanvasWm.Core.Tests.ProjectionSpec.Pj;

namespace CanvasWm.Core.Tests.ProjectionSpec;

public class StretchViewTests
{
    private static StretchBreakpoint B(double f, double t) => new(f, t);

    [Fact]
    public void AC1_StageAndGapClamp()
    {
        Assert.Equal(R(5, 30, 1430, 865), ScreenProjection.Stage(A, 10));
        Assert.Equal(A, ScreenProjection.Stage(A, 0));
        foreach (var g in new[] { double.NaN, -3, double.PositiveInfinity })
        {
            var v = new ProjectionView(A, g, R(0, 0, 1, 1), Stretch.Identity);
            Assert.Equal(0, v.Gap);
            Assert.Equal(A, v.Stage);
            Assert.Equal(A, ScreenProjection.Stage(A, g));
        }
        Assert.Equal(R(5, 5, 0, 0), ScreenProjection.Stage(R(0, 0, 6, 6), 10));
        Assert.Equal(R(5, 30, 1430, 865), new ProjectionView(A, 10, R(0, 0, 1, 1), Stretch.Identity).Stage);
        foreach (var cam in new[] { R(0, 0, 0, 1), R(0, 0, 1, -1), R(0, 0, double.NaN, 1) })
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectionView(A, 10, cam, Stretch.Identity));
    }

    [Fact]
    public void AC2_ForFramingAndMap()
    {
        Assert.Equal(Stretch.Identity, Stretch.ForFraming(null));
        Assert.Equal(Stretch.Identity, Stretch.ForFraming(R(0, 0, 0, 1)));
        Assert.Equal(Stretch.Identity, Stretch.ForFraming(R(0, 0, .5, .5)));
        Assert.Equal(Stretch.Identity, Stretch.ForFraming(R(0, 0, .5, .5 + 1e-10)));

        var s1 = Stretch.ForFraming(R(0, 0, .5, 1));
        Assert.Equal(new[] { B(0, 0), B(.5, 1) }, s1.X.ToArray());
        Assert.Empty(s1.Y);
        var s2 = Stretch.ForFraming(R(.5, 0, .5, 1));
        Assert.Equal(new[] { B(.5, .5), B(1, 1.5) }, s2.X.ToArray());
        var s3 = Stretch.ForFraming(R(0, 0, 1, .5));
        Assert.Equal(new[] { B(0, 0), B(.5, 1) }, s3.Y.ToArray());
        Assert.Empty(s3.X);

        Near(0, s2.MapX(0)); Near(.5, s2.MapX(.5)); Near(1, s2.MapX(.75));
        Near(1.5, s2.MapX(1)); Near(1.7, s2.MapX(1.2));
        foreach (var v in new[] { -1.0, 0, .3, 2 }) Near(v, s2.MapY(v));

        var shift = new Stretch(new[] { B(0, .25) }, Array.Empty<StretchBreakpoint>());
        foreach (var v in new[] { -2.0, 0, .5, 7 }) Near(v + .25, shift.MapX(v));

        var none = Array.Empty<StretchBreakpoint>();
        Assert.Throws<ArgumentException>(() => new Stretch(new[] { B(1, 0), B(1, 2) }, none));
        Assert.Throws<ArgumentException>(() => new Stretch(new[] { B(1, 0), B(0, 2) }, none));
        Assert.Throws<ArgumentException>(() => new Stretch(new[] { B(double.NaN, 0) }, none));

        var e1 = new Stretch(new[] { B(.5, .5), B(1, 1.5) }, none);
        var e2 = new Stretch(new[] { B(.5, .5), B(1, 1.5) }, none);
        Assert.True(e1.Equals(e2));
        Assert.Equal(e1, e2);
    }

    [Fact]
    public void AC3_CameraForFraming()
    {
        Assert.Equal(R(0, 0, 1, 1), ScreenProjection.CameraForFraming(null));
        Assert.Equal(R(0, 0, 1, 1), ScreenProjection.CameraForFraming(R(0, 0, .5, 1)));
        Assert.Equal(R(.5, 0, 1, 1), ScreenProjection.CameraForFraming(R(.5, 0, .5, 1)));
        Assert.Equal(R(.5, 0, .5, .5), ScreenProjection.CameraForFraming(R(.5, 0, .5, .5)));
        Assert.Equal(R(0, 0, 1, 1), ScreenProjection.CameraForFraming(R(0, 0, 0, 1)));
        foreach (var f in new[] { R(0, 0, .5, 1), R(.5, 0, .5, 1), R(.5, 0, .5, .5) })
            Near(Stage, ScreenProjection.Project(ProjectionView.AtRest(A, 10, f), f));
    }
}
