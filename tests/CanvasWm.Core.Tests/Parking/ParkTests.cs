using CanvasWm.Core.Geometry;
using CanvasWm.Core.Parking;

namespace CanvasWm.Core.Tests.ParkingSpec;

public class ParkTests
{
    private static readonly Rect D = new(0, 0, 1440, 900);
    private static readonly Rect V = new(0, 25, 1440, 875);
    private const double Tol = 1e-12;

    private static ParkDisplay Disp(params Rect[] others) => new(V, D, others, new Point(0, 0));
    private static Size S(double w, double h) => new(w, h);
    private static Rect R(double x, double y, double w, double h) => new(x, y, w, h);

    // ---- AC1
    [Fact]
    public void AC1_SingleDisplayFits()
    {
        Assert.Equal(S(1000, 700), ParkGeometry.ParkSize(S(1000, 700), V, null));
        Assert.Equal(R(1439, 899, 1000, 700), ParkGeometry.ParkFrame(Disp(), S(1000, 700), null));
    }

    // ---- AC2
    [Fact]
    public void AC2_TooBigAndMinimumFloor()
    {
        Assert.Equal(S(1440, 720), ParkGeometry.ParkSize(S(2000, 1000), V, null));
        Assert.Equal(R(1439, 899, 1440, 720), ParkGeometry.ParkFrame(Disp(), S(2000, 1000), null));
        Assert.Equal(R(1439, 899, 1500, 800), ParkGeometry.ParkFrame(Disp(), S(2000, 1000), S(1500, 800)));
        Assert.Equal(R(1439, 899, 1500, 800), ParkGeometry.ParkFrame(Disp(), S(2000, 1000), S(1499.2, 799.5)));
    }

    // ---- AC3
    [Fact]
    public void AC3_ParkSmall()
    {
        Assert.Equal(S(417, 292), ParkGeometry.ParkSmallSize(S(1000, 700), V));
        var d = Disp();
        Assert.Equal(R(1439, 899, 417, 292), ParkGeometry.ParkSmallFrame(d, S(1000, 700)));
        Assert.Equal(ParkGeometry.ParkFrame(d, S(417, 292), null), ParkGeometry.ParkSmallFrame(d, S(1000, 700)));
    }

    // ---- AC4
    [Fact]
    public void AC4_DisplayToTheRight()
    {
        var others = new[] { R(1440, 0, 1920, 1080) };
        var c = ParkGeometry.Candidates(D, S(800, 600));
        Assert.Equal(191760, ParkGeometry.Score(c[0].Reach, others));
        Assert.Equal(0, ParkGeometry.Score(c[1].Reach, others));
        Assert.Equal(1, ParkGeometry.ChooseCandidate(c, others));
        Assert.Equal(R(-799, 899, 800, 600), ParkGeometry.ParkFrame(Disp(others), S(800, 600), null));
    }

    // ---- AC5
    [Fact]
    public void AC5_Surrounded()
    {
        var d = new Rect(0, 0, 1000, 800);
        var others = new[] { R(1000, 0, 1000, 800), R(-1000, 0, 1000, 800), R(0, 800, 1000, 800) };
        var disp = new ParkDisplay(d, d, others, new Point(0, 0));
        var c = ParkGeometry.Candidates(d, S(500, 400));
        double[] expected = { 30340, 30340, 229540, 229540, 200000 };
        for (int i = 0; i < 5; i++) Assert.Equal(expected[i], ParkGeometry.Score(c[i].Reach, others));
        Assert.Equal(0, ParkGeometry.ChooseCandidate(c, others));
        Assert.Equal(R(999, 799, 500, 400), ParkGeometry.ParkFrame(disp, S(500, 400), null));
    }

    // ---- AC6
    [Fact]
    public void AC6_DisplayBelow()
    {
        var others = new[] { R(0, 900, 1440, 900) };
        var c = ParkGeometry.Candidates(D, S(800, 600));
        Assert.Equal(600, ParkGeometry.Score(c[0].Reach, others));
        Assert.Equal(600, ParkGeometry.Score(c[1].Reach, others));
        Assert.Equal(0, ParkGeometry.Score(c[2].Reach, others));
        Assert.Equal(R(1439, 0, 800, 600), ParkGeometry.ParkFrame(Disp(others), S(800, 600), null));
    }

    // ---- AC7
    [Fact]
    public void AC7_NoDisplayFrame()
    {
        var o = new Point(2559, 1439);
        var d1 = new ParkDisplay(V, null, new[] { R(5000, 5000, 100, 100) }, o);
        var d2 = new ParkDisplay(V, null, new[] { R(0, 0, 9999, 9999) }, o);
        Assert.Equal(R(2559, 1439, 800, 600), ParkGeometry.ParkFrame(d1, S(800, 600), null));
        Assert.Equal(R(2559, 1439, 800, 600), ParkGeometry.ParkFrame(d2, S(800, 600), null));
    }

    // ---- AC8
    [Fact]
    public void AC8_IsParkFrame()
    {
        var d = Disp();
        Assert.True(ParkGeometry.IsParkFrame(d, R(1439, 899, 1000, 700)));
        Assert.True(ParkGeometry.IsParkFrame(d, R(1439, 899, 2000, 1000)));
        Assert.False(ParkGeometry.IsParkFrame(d, R(1438, 899, 1000, 700)));
        Assert.True(ParkGeometry.IsParkFrame(d, R(1439, 899, 1000.5, 700)));
        Assert.False(ParkGeometry.IsParkFrame(d, R(1439, 899.5, 1000, 700)));
    }

    // ---- AC9
    private static bool Rests(Rect actual, Rect target, double expectedFraction)
    {
        double f = ParkGeometry.VisibleFraction(actual, new[] { D });
        Assert.Equal(expectedFraction, f, Tol);
        return ParkGeometry.RestsParked(actual, target, f);
    }

    [Fact]
    public void AC9_RestsParked()
    {
        var t1 = R(1439, 899, 1000, 700);
        Assert.False(Rests(R(1439, 899, 990, 700), t1, ParkGeometry.VisibleFraction(R(1439, 899, 990, 700), new[] { D })));
        Assert.False(Rests(R(1300, 600, 1000, 700), t1, 0.06));
        Assert.True(Rests(R(1439.5, 860, 1000, 700), t1, 20.0 / 700000));
        var t2 = R(1439, 899, 10, 100);
        Assert.True(Rests(R(1439, 840, 10, 100), t2, 0.06));
        Assert.False(Rests(R(1439, 830, 10, 100), t2, 0.07));
    }

    // ---- AC10
    [Fact]
    public void AC10_CandidateTable()
    {
        var c = ParkGeometry.Candidates(D, S(800, 600));
        Assert.Equal(5, c.Count);
        Rect[] spots = { R(1439, 899, 800, 600), R(-799, 899, 800, 600), R(1439, 0, 800, 600), R(-799, 0, 800, 600), R(0, 899, 800, 600) };
        Rect[] reach = { R(1439, 840, 800, 660), R(-799, 840, 800, 660), R(1439, 0, 800, 660), R(-799, 0, 800, 660), R(0, 840, 800, 660) };
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(spots[i], c[i].Spot);
            Assert.Equal(reach[i], c[i].Reach);
        }
    }

    // ---- AC11
    [Fact]
    public void AC11_CustomOptions_Candidates()
    {
        var c = ParkGeometry.Candidates(D, S(800, 600), new ParkOptions { EdgeOverlap = 0, Reach = 0 });
        Rect[] spots = { R(1440, 900, 800, 600), R(-800, 900, 800, 600), R(1440, 0, 800, 600), R(-800, 0, 800, 600), R(0, 900, 800, 600) };
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(spots[i], c[i].Spot);
            Assert.Equal(c[i].Spot, c[i].Reach);
        }
    }

    [Fact]
    public void AC11_CustomOptions_RestsParked()
    {
        var o = new ParkOptions { Reach = 30, Tolerance = 2 };
        var t = R(0, 100, 10, 10);
        Assert.True(ParkGeometry.RestsParked(R(1.9, 68, 10, 10), t, 1, o));
        Assert.False(ParkGeometry.RestsParked(R(1.9, 67.9, 10, 10), t, 1, o));
        Assert.True(ParkGeometry.RestsParked(R(1.9, 102, 10, 10), t, 1, o));
        Assert.False(ParkGeometry.RestsParked(R(1.9, 102.1, 10, 10), t, 1, o));
    }

    // ---- AC12
    [Fact]
    public void AC12_PositionsNotRounded()
    {
        var d = new ParkDisplay(new Rect(0, 0, 1000, 800), new Rect(0.5, 0.25, 1000.5, 800.25), Array.Empty<Rect>(), new Point(0, 0));
        Assert.Equal(R(1000, 799.5, 100, 100), ParkGeometry.ParkFrame(d, S(100, 100), null));
    }

    // ---- AC13
    [Fact]
    public void AC13_RoundingMode() => Assert.Equal(S(2, 3), ParkGeometry.FitWithin(S(4, 5), 2, 100));

    // ---- AC14
    [Fact]
    public void AC14_NeverEnlargeNoRounding()
    {
        Assert.Equal(S(100.3, 50.7), ParkGeometry.FitWithin(S(100.3, 50.7), 1440, 875));
        Assert.Equal(S(1440, 875), ParkGeometry.FitWithin(S(1440, 875), 1440, 875));
        Assert.Equal(S(300, 200), ParkGeometry.ParkSize(S(300, 200), V, null));
    }

    // ---- AC15
    [Fact]
    public void AC15_DegenerateAxis()
    {
        Assert.Equal(S(0, 875), ParkGeometry.FitWithin(S(1, 2000), 1440, 875));
        Assert.Equal(S(0.5, 0.5), ParkGeometry.FitWithin(S(0.5, 0.5), 10, 10));
        Assert.Equal(S(0, 875), ParkGeometry.FitWithin(S(0, 2000), 1440, 875));
        Assert.Equal(S(1440, 1), ParkGeometry.FitWithin(S(2000, 1), 1440, 875));
    }

    // ---- AC16
    [Fact]
    public void AC16_MinimumAfterFit()
    {
        Assert.Equal(S(1440, 901), ParkGeometry.ParkSize(S(2000, 1000), V, S(100, 900.1)));
        Assert.Equal(S(600, 400), ParkGeometry.ParkSize(S(500, 400), V, S(600, 0)));
        Assert.Equal(S(500, 400), ParkGeometry.ParkSize(S(500, 400), V, null));
    }

    // ---- AC17
    [Fact]
    public void AC17_ParkSmallVariants()
    {
        Assert.Equal(S(300, 200), ParkGeometry.ParkSmallSize(S(300, 200), V));
        Assert.Equal(S(625, 438), ParkGeometry.ParkSmallSize(S(1000, 700), V, new ParkOptions { SmallDivisor = 2 }));
        var d = Disp();
        var small = ParkGeometry.ParkSmallFrame(d, S(300, 200));
        Assert.Equal(S(300, 200), new Size(small.Width, small.Height));
        // ParkFrame with a large minimum differs; ParkSmallFrame ignores any minimum.
        var big = ParkGeometry.ParkFrame(d, S(300, 200), S(1000, 800));
        Assert.Equal(S(1000, 800), new Size(big.Width, big.Height));
        Assert.NotEqual(big, small);
    }

    // ---- AC18
    [Fact]
    public void AC18_ScoringHelpers()
    {
        Assert.Equal(0, ParkGeometry.IntersectionArea(R(0, 0, 10, 10), R(10, 0, 10, 10)));
        Assert.Equal(25, ParkGeometry.IntersectionArea(R(0, 0, 10, 10), R(5, 5, 10, 10)));
        Assert.Equal(0, ParkGeometry.IntersectionArea(R(0, 0, 0, 10), R(-5, -5, 20, 20)));
        Assert.Equal(0, ParkGeometry.Score(R(0, 0, 10, 10), Array.Empty<Rect>()));
        var c = ParkGeometry.Candidates(D, S(800, 600));
        Assert.Equal(0, ParkGeometry.ChooseCandidate(c, Array.Empty<Rect>()));

        // reach scores [5,3,3,4,9] against one other rect (unit height strips of widths).
        double[] w = { 5, 3, 3, 4, 9 };
        var cands = new List<ParkCandidate>();
        for (int i = 0; i < 5; i++)
            cands.Add(new ParkCandidate(R(0, 0, 1, 1), R(0, 0, w[i], 1)));
        var other = new[] { R(0, 0, 100, 1) };
        for (int i = 0; i < 5; i++) Assert.Equal(w[i], ParkGeometry.Score(cands[i].Reach, other));
        Assert.Equal(1, ParkGeometry.ChooseCandidate(cands, other));

        Assert.Throws<ArgumentException>(() => ParkGeometry.ChooseCandidate(new List<ParkCandidate>(), other));
    }

    [Fact]
    public void AC18_FirstZeroWinsOverLowerLaterNonZero_AndNanRectScoresZero()
    {
        // B12 NaN sentence: IntersectionArea cannot produce a NaN score (NaN extents fail the
        // w>0&&h>0 test and score 0.0), so pin the reachable behaviour: a NaN reach rect scores
        // exactly 0.0 and is returned as the first zero.
        var other = new[] { R(0, 0, 100, 100) };
        var nan = R(double.NaN, 0, 10, 10);
        var pos = R(0, 0, 10, 10);
        Assert.Equal(0.0, ParkGeometry.Score(nan, other));
        var cands = new[]
        {
            new ParkCandidate(pos, pos), new ParkCandidate(nan, nan), new ParkCandidate(pos, R(0, 0, 1, 1)),
        };
        Assert.Equal(1, ParkGeometry.ChooseCandidate(cands, other));
        var cands2 = new[] { new ParkCandidate(nan, nan), new ParkCandidate(pos, pos) };
        Assert.Equal(0, ParkGeometry.ChooseCandidate(cands2, other));
    }

    // ---- AC19
    [Fact]
    public void AC19_RestsParkedBoundaries()
    {
        var t = R(0, 100, 10, 10);
        Assert.False(ParkGeometry.RestsParked(R(0, 100, 11, 10), t, 1));
        Assert.False(ParkGeometry.RestsParked(R(0, 100, 10, 11), t, 1));
        Assert.True(ParkGeometry.RestsParked(R(0, 100, 10.999, 10), t, 1));
        Assert.False(ParkGeometry.RestsParked(R(5, 100, 10, 10), t, 0.05));
        Assert.True(ParkGeometry.RestsParked(R(5, 100, 10, 10), t, 0.0499));
        Assert.True(ParkGeometry.RestsParked(R(0, 39, 10, 10), t, 1));
        Assert.False(ParkGeometry.RestsParked(R(0, 38.999, 10, 10), t, 1));
        Assert.True(ParkGeometry.RestsParked(R(0, 101, 10, 10), t, 1));
        Assert.False(ParkGeometry.RestsParked(R(0, 101.001, 10, 10), t, 1));
        Assert.False(ParkGeometry.RestsParked(R(1, 100, 10, 10), t, 1));
        Assert.False(ParkGeometry.RestsParked(R(0, 100, double.NaN, 10), t, 0));
        Assert.True(ParkGeometry.RestsParked(R(0, 100, 10, 10), t, double.NaN));
        foreach (double f in new[] { 0, 0.05, 0.5, 1 })
            Assert.True(ParkGeometry.RestsParked(t, t, f));
    }

    // ---- AC20
    [Fact]
    public void AC20_IsParkFrameChangedDisplaysAndNaN()
    {
        var f = R(1439, 899, 800, 600);
        Assert.True(ParkGeometry.IsParkFrame(Disp(), f));
        Assert.False(ParkGeometry.IsParkFrame(Disp(R(1440, 0, 1920, 1080)), f));
        Assert.False(ParkGeometry.IsParkFrame(Disp(), R(double.NaN, 899, 800, 600)));
        Assert.False(ParkGeometry.IsParkFrame(Disp(), R(1439, double.NaN, 800, 600)));
        Assert.False(ParkGeometry.IsParkFrame(Disp(), R(1439, 899, double.NaN, 600)));
        Assert.False(ParkGeometry.IsParkFrame(Disp(), R(1439, 899, 800, double.NaN)));
        var nf = new ParkDisplay(V, null, Array.Empty<Rect>(), new Point(2559, 1439));
        Assert.True(ParkGeometry.IsParkFrame(nf, R(2559, 1439, 800, 600)));
        Assert.False(ParkGeometry.IsParkFrame(nf, R(2559, 1440, 800, 600)));
    }

    // ---- AC21
    [Fact]
    public void AC21_VisibleFraction()
    {
        var s = new[] { R(0, 0, 100, 100) };
        Assert.Equal(1, ParkGeometry.VisibleFraction(R(10, 10, 20, 20), s), Tol);
        Assert.Equal(0.5, ParkGeometry.VisibleFraction(R(90, 0, 20, 100), s), Tol);
        Assert.Equal(0, ParkGeometry.VisibleFraction(R(10, 10, 20, 20), Array.Empty<Rect>()));
        Assert.Equal(0, ParkGeometry.VisibleFraction(R(10, 10, 0, 20), s));
        Assert.Equal(0, ParkGeometry.VisibleFraction(R(10, 10, 20, -5), s));
        Assert.Equal(2, ParkGeometry.VisibleFraction(R(10, 10, 20, 20), new[] { s[0], s[0] }), Tol);
        Assert.Equal(1, ParkGeometry.VisibleFraction(R(90, 10, 20, 20), new[] { s[0], R(100, 0, 100, 100) }), Tol);
    }

    // ---- AC22
    [Fact]
    public void AC22_DefaultFallbackOrigin()
    {
        var a = new[] { R(0, 0, 1440, 900), R(1440, 0, 1920, 1080) };
        Assert.Equal(new Point(3359, 1079), ParkGeometry.DefaultFallbackOrigin(a));
        Assert.Equal(new Point(999, 799), ParkGeometry.DefaultFallbackOrigin(new[] { R(0, 0, 1000, 800), R(-1000, 0, 2000, 600) }));
        Assert.Equal(new Point(10000, 10000), ParkGeometry.DefaultFallbackOrigin(Array.Empty<Rect>()));
        Assert.Equal(new Point(3360, 1080), ParkGeometry.DefaultFallbackOrigin(a, new ParkOptions { EdgeOverlap = 0 }));
    }

    // ---- AC23
    [Fact]
    public void AC23_OptionsDefaults()
    {
        var o = ParkOptions.Default;
        Assert.Equal(1, o.EdgeOverlap);
        Assert.Equal(60, o.Reach);
        Assert.Equal(1, o.Tolerance);
        Assert.Equal(0.05, o.VisibleFractionThreshold);
        Assert.Equal(3, o.SmallDivisor);
        Assert.Equal(ParkOptions.Default, new ParkOptions());
    }

    [Fact]
    public void AC23_OptionsInvalidThrow()
    {
        var ex = new List<Action>
        {
            () => _ = new ParkOptions { EdgeOverlap = -0.1 },
            () => _ = new ParkOptions { EdgeOverlap = double.NaN },
            () => _ = new ParkOptions { EdgeOverlap = double.PositiveInfinity },
            () => _ = new ParkOptions { Reach = -1 },
            () => _ = new ParkOptions { Reach = double.NaN },
            () => _ = new ParkOptions { Tolerance = 0 },
            () => _ = new ParkOptions { Tolerance = -1 },
            () => _ = new ParkOptions { Tolerance = double.NaN },
            () => _ = new ParkOptions { VisibleFractionThreshold = -0.01 },
            () => _ = new ParkOptions { VisibleFractionThreshold = 1.01 },
            () => _ = new ParkOptions { VisibleFractionThreshold = double.NaN },
            () => _ = new ParkOptions { SmallDivisor = 0.5 },
            () => _ = new ParkOptions { SmallDivisor = double.NaN },
            () => _ = new ParkOptions { SmallDivisor = double.PositiveInfinity },
        };
        foreach (var a in ex) Assert.Throws<ArgumentOutOfRangeException>(a);
    }

    [Fact]
    public void AC23_OptionsBoundariesAccepted()
    {
        _ = new ParkOptions { EdgeOverlap = 0 };
        _ = new ParkOptions { Reach = 0 };
        _ = new ParkOptions { VisibleFractionThreshold = 0 };
        _ = new ParkOptions { VisibleFractionThreshold = 1 };
        _ = new ParkOptions { SmallDivisor = 1 };
    }

    // ---- AC24
    [Fact]
    public void AC24_NullArguments()
    {
        var d = Disp();
        var c = ParkGeometry.Candidates(D, S(10, 10));
        Assert.Throws<ArgumentNullException>(() => new ParkDisplay(V, D, null!, new Point(0, 0)));
        Assert.Throws<ArgumentNullException>(() => ParkGeometry.ParkFrame(null!, S(1, 1), null));
        Assert.Throws<ArgumentNullException>(() => ParkGeometry.ParkSmallFrame(null!, S(1, 1)));
        Assert.Throws<ArgumentNullException>(() => ParkGeometry.IsParkFrame(null!, R(0, 0, 1, 1)));
        Assert.Throws<ArgumentNullException>(() => ParkGeometry.Score(R(0, 0, 1, 1), null!));
        Assert.Throws<ArgumentNullException>(() => ParkGeometry.ChooseCandidate(null!, Array.Empty<Rect>()));
        Assert.Throws<ArgumentNullException>(() => ParkGeometry.ChooseCandidate(c, null!));
        Assert.Throws<ArgumentNullException>(() => ParkGeometry.VisibleFraction(R(0, 0, 1, 1), null!));
        Assert.Throws<ArgumentNullException>(() => ParkGeometry.DefaultFallbackOrigin(null!));
        Assert.NotNull(d);
    }

    [Fact]
    public void AC24_SnapshotIsolatedFromSourceList()
    {
        var list = new List<Rect> { R(1440, 0, 1920, 1080) };
        var d = new ParkDisplay(V, D, list, new Point(0, 0));
        var before = ParkGeometry.ParkFrame(d, S(800, 600), null);
        list.Clear();
        Assert.Single(d.OtherDisplays);
        Assert.Equal(before, ParkGeometry.ParkFrame(d, S(800, 600), null));
        Assert.Equal(R(-799, 899, 800, 600), before);
    }

    // ---- AC26 (reference hygiene part verifiable here)
    [Fact]
    public void AC26_CoreHasNoNewReferences()
    {
        var refs = typeof(ParkGeometry).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain(refs, n => n!.Contains("Win32") || n.Contains("Engine"));
    }
}

public class ParkGeometryPropertyTests
{
    private static Rect Own(Rect a, Rect b)
    {
        double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        double mx = Math.Min(a.X + a.Width, b.X + b.Width), my = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return new Rect(x, y, Math.Max(0, mx - x), Math.Max(0, my - y));
    }

    private static bool Same(Rect a, Rect b) => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;

    [Fact]
    public void AC25_SeededProperties()
    {
        for (int seed = 1; seed <= 200; seed++)
        {
            var rnd = new Random(seed);
            for (int n = 0; n < 50; n++)
            {
                string ctx = $"seed {seed} case {n}";
                var D = new Rect(rnd.Next(-4000, 4001), rnd.Next(-4000, 4001), rnd.Next(400, 3001), rnd.Next(400, 3001));
                var V = new Rect(0, 0, D.Width, D.Height - rnd.Next(0, 81));
                var others = new List<Rect>();
                int k0 = rnd.Next(0, 5);
                for (int i = 0; i < k0; i++)
                    others.Add(new Rect(rnd.Next(-8000, 8001), rnd.Next(-8000, 8001), rnd.Next(200, 3001), rnd.Next(200, 3001)));
                var size = new Size(rnd.Next(50, 4001), rnd.Next(50, 4001));
                Size? min = rnd.Next(2) == 0 ? new Size(rnd.Next(0, 3501), rnd.Next(0, 3501)) : null;
                bool noFrame = rnd.Next(100) < 20;
                var origin = new Point(rnd.Next(-8000, 8001), rnd.Next(-8000, 8001));
                ParkOptions? opts = null;
                if (rnd.Next(2) == 0)
                    opts = new ParkOptions { EdgeOverlap = rnd.Next(0, 4), Reach = rnd.Next(0, 101) };
                var o = (opts ?? ParkOptions.Default).EdgeOverlap;

                var disp = new ParkDisplay(V, noFrame ? null : D, others, origin);
                var frame = ParkGeometry.ParkFrame(disp, size, min, opts);
                var ps = ParkGeometry.ParkSize(size, V, min);
                double W = ps.Width, H = ps.Height;

                IReadOnlyList<ParkCandidate>? c = null;
                int k = -1;
                if (!noFrame)
                {
                    c = ParkGeometry.Candidates(D, ps, opts);
                    // P3
                    k = -1;
                    for (int i = 0; i < 5; i++)
                        if (ParkGeometry.Score(c[i].Reach, others) == 0.0) { k = i; break; }
                    if (k < 0)
                    {
                        double best = double.MaxValue;
                        for (int i = 0; i < 5; i++)
                        {
                            double s = ParkGeometry.Score(c[i].Reach, others);
                            if (s < best) { best = s; k = i; }
                        }
                    }
                    Assert.True(Same(c[k].Spot, frame), ctx + " P3");
                    Assert.Equal(k, ParkGeometry.ChooseCandidate(c, disp.OtherDisplays));

                    // P1
                    if (o >= 1 && o <= W && W <= D.Width && o <= H && H <= D.Height)
                    {
                        var I = Own(frame, D);
                        double expected = k <= 1 ? o * o : k <= 3 ? o * H : W * o;
                        Assert.True(Math.Abs(I.Width * I.Height - expected) < 1e-9, ctx + $" P1 k={k}");
                        bool contained = frame.X >= D.X && frame.Y >= D.Y
                            && frame.X + frame.Width <= D.X + D.Width && frame.Y + frame.Height <= D.Y + D.Height;
                        Assert.False(contained, ctx + " P1 contained");
                    }
                }
                else
                {
                    Assert.True(Same(frame, new Rect(origin.X, origin.Y, W, H)), ctx + " fallback");
                }

                // P2
                Assert.True(ParkGeometry.IsParkFrame(disp, frame, opts), ctx + " P2a");
                Assert.True(ParkGeometry.IsParkFrame(disp, ParkGeometry.ParkSmallFrame(disp, size, opts), opts), ctx + " P2b");

                // P4
                var disp2 = new ParkDisplay(V, noFrame ? null : D, others.ToArray(), origin);
                Assert.True(Same(frame, ParkGeometry.ParkFrame(disp2, size, min, opts)), ctx + " P4a");
                Assert.Equal(ParkGeometry.IsParkFrame(disp, frame, opts), ParkGeometry.IsParkFrame(disp2, frame, opts));
                Assert.Equal(ParkGeometry.VisibleFraction(frame, others), ParkGeometry.VisibleFraction(frame, others));
                if (!noFrame)
                {
                    var c2 = ParkGeometry.Candidates(D, ps, opts);
                    for (int i = 0; i < 5; i++)
                    {
                        Assert.True(Same(c![i].Spot, c2[i].Spot) && Same(c[i].Reach, c2[i].Reach), ctx + " P4c");
                    }
                }

                // P5
                if (min is null)
                {
                    Assert.True(W >= 0 && W <= size.Width && H >= 0 && H <= size.Height, ctx + " P5a");
                    Assert.True((W == size.Width && H == size.Height) || (W <= V.Width + 0.5 && H <= V.Height + 0.5), ctx + " P5b");
                }

                // P6
                foreach (double x in new[] { 0, rnd.NextDouble(), 1 })
                    Assert.True(ParkGeometry.RestsParked(frame, frame, x, opts), ctx + " P6");
            }
        }
    }
}
