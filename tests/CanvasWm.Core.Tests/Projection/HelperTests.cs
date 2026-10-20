using CanvasWm.Core.Geometry;
using CanvasWm.Core.Projection;
using static CanvasWm.Core.Tests.ProjectionSpec.Pj;

namespace CanvasWm.Core.Tests.ProjectionSpec;

public class HelperTests
{
    private static readonly Size M = S(200, 120);

    [Fact]
    public void AC11_OnStageFraction()
    {
        Near(702.0 / 705, ScreenProjection.OnStageFraction(R(733, 35, 705, 855), Stage));
        Assert.True(ScreenProjection.OnStageFraction(R(733, 35, 705, 855), Stage) >= 0.995);
        Near(701.0 / 705, ScreenProjection.OnStageFraction(R(734, 35, 705, 855), Stage));
        Assert.True(ScreenProjection.OnStageFraction(R(734, 35, 705, 855), Stage) < 0.995);
        Assert.Equal(0, ScreenProjection.OnStageFraction(R(10, 35, 0, 855), Stage));
        Assert.Equal(0, ScreenProjection.OnStageFraction(R(10, 35, 100, -5), Stage));
        Assert.Equal(0, ScreenProjection.OnStageFraction(R(10, 35, double.NaN, 855), Stage));
        Assert.Equal(0, ScreenProjection.OnStageFraction(R(1435, 35, 100, 100), Stage));
    }

    [Fact]
    public void AC13_FitsAndPreviewDefaults()
    {
        Assert.Equal(M, ScreenProjection.EffectiveMinimum(false, null, Tuning.Default));
        Assert.False(ScreenProjection.Fits(R(0, 0, 190, 400), M));
        Assert.Equal(S(200, 421), ScreenProjection.PreviewSize(R(0, 0, 190, 400), M, null));
        Assert.True(ScreenProjection.Fits(R(0, 0, 199.6, 400), M));
        Assert.True(ScreenProjection.Fits(R(0, 0, 199.5, 119.5), M));
        Assert.False(ScreenProjection.Fits(R(0, 0, 199.4, 400), M));
    }

    [Fact]
    public void AC14_FixedMinimum()
    {
        Assert.Equal(S(480, 320), ScreenProjection.EffectiveMinimum(true, null, Tuning.Default));
        Assert.False(ScreenProjection.Fits(R(725, 468, 348, 206), S(480, 320)));
        Assert.Equal(S(541, 320), ScreenProjection.PreviewSize(R(725, 468, 348, 206), S(480, 320), null));
        var t = Tuning.Default with { FixedTileMinimum = S(500, 100) };
        Assert.Equal(S(500, 100), ScreenProjection.EffectiveMinimum(true, null, t));
    }

    [Fact]
    public void AC15_OwnMinimumAndPreview()
    {
        var t = Tuning.Default;
        Assert.Equal(S(400, 300), ScreenProjection.EffectiveMinimum(false, S(400, 300), t));
        Assert.Equal(S(200, 500), ScreenProjection.EffectiveMinimum(false, S(150, 500), t));
        Assert.Equal(S(200, 120), ScreenProjection.EffectiveMinimum(false, S(double.NaN, -5), t));
        Assert.Equal(S(507, 300), ScreenProjection.PreviewSize(R(0, 0, 348, 206), S(400, 300), null));
        Assert.Equal(S(640, 480), ScreenProjection.PreviewSize(R(0, 0, 1, 400), M, S(640, 480)));
        Assert.Equal(S(800, 600), ScreenProjection.PreviewSize(R(0, 0, 1, 400), M, null));
        Assert.Equal(S(500, 300), ScreenProjection.PreviewSize(R(0, 0, 500, 300), S(200, 120), null));
    }

    [Fact]
    public void AC17_ClampToMaximum()
    {
        var f = R(10, 35, 705, 855);
        var c = ScreenProjection.ClampToMaximum(f, S(600, 700));
        Near(R(62.5, 112.5, 600, 700), c);
        Assert.Equal(R(63, 113, 600, 700), ScreenProjection.RoundEdges(c));
        Assert.Equal(f, ScreenProjection.ClampToMaximum(f, S(800, 0)));
        Assert.Equal(f, ScreenProjection.ClampToMaximum(f, S(double.NaN, 900)));
        Near(R(10, 112.5, 705, 700), ScreenProjection.ClampToMaximum(f, S(800, 700)));
        Assert.Equal(f, ScreenProjection.ClampToMaximum(f, null));
    }

    [Fact]
    public void AC17_EndToEnd_Maximum()
    {
        var r = Run(Ab(), null, k => k == "a" ? With(w => w with { Maximum = S(600, 700) }) : Std);
        Assert.Equal(new WindowTarget(TargetKind.Tile, R(63, 113, 600, 700)), r.Targets["a"]);
    }

    [Fact]
    public void AC19_StripRectAndHeight()
    {
        Near(R(18, 35, 689, 15), ScreenProjection.StripRect(R(10, 35, 705, 100), 28, 8));
        Near(R(5, 0, 120, 28), ScreenProjection.StripRect(R(0, 0, 130, 855), 28, 8));
        Near(R(0, 0, 100, 28), ScreenProjection.StripRect(R(0, 0, 100, 855), 28, 8));
        Near(R(0, 0, 705, 28), ScreenProjection.StripRect(R(0, 0, 705, 855), 28, -3));
        Near(R(0, 0, 705, 28), ScreenProjection.StripRect(R(0, 0, 705, 855), 28, double.NaN));
        Assert.Equal(28, ScreenProjection.StripFullHeight(4, Tuning.Default));
        Assert.Equal(20, ScreenProjection.StripFullHeight(-1, Tuning.Default));
        Assert.Equal(38, ScreenProjection.StripFullHeight(4, Tuning.Default with { TabStripContentHeight = 30 }));
    }

    [Fact]
    public void AC21_RoundEdges()
    {
        Assert.Equal(R(1, -1, 1, 2), ScreenProjection.RoundEdges(R(0.5, -0.5, 1, 1)));
        Assert.Equal(R(3, 0, 0, 0), ScreenProjection.RoundEdges(R(2.5, 0, 0.2, 0)));
        Assert.Equal(R(-2, 0, 1, 1), ScreenProjection.RoundEdges(R(-1.5, 0, 1, 1)));
        Assert.Equal(R(10, 35, 706, 855), ScreenProjection.RoundEdges(R(10, 35, 705.5, 855)));
    }

    [Fact]
    public void AC22_FitAndGrow()
    {
        Near(R(100, 50, 400, 200), ScreenProjection.FitAspect(R(100, 50, 400, 300), S(800, 400)));
        Near(R(225, 50, 150, 300), ScreenProjection.FitAspect(R(100, 50, 400, 300), S(300, 600)));
        var r = R(100, 50, 400, 300);
        Assert.Equal(r, ScreenProjection.FitAspect(r, S(0, 600)));
        var z = R(100, 50, 0, 300);
        Assert.Equal(z, ScreenProjection.FitAspect(z, S(800, 400)));

        Near(R(50, 50, 200, 120), ScreenProjection.GrowToMinimum(R(100, 50, 100, 60), M, Stage));
        Near(R(1235, 775, 200, 120), ScreenProjection.GrowToMinimum(R(1400, 800, 100, 60), M, Stage));
        Near(R(199.6, 50, 199.6, 120), ScreenProjection.GrowToMinimum(R(199.6, 50, 199.6, 120), M, Stage));
        Near(R(5, 37, 1430, 858), ScreenProjection.GrowToMinimum(R(100, 50, 100, 60), S(3000, 120), Stage));
    }
}
