using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Parking;

/// <summary>Pure park geometry: sizes, candidate spots, scoring, recognisers and measures.</summary>
public static class ParkGeometry
{
    /// <summary>Window dimensions at or below this value (1) are not used as a divisor in
    /// FitWithin.</summary>
    public const double DegenerateDimension = 1.0;

    /// <summary>Origin returned by DefaultFallbackOrigin when there are no screens:
    /// (10000, 10000).</summary>
    public static Point NoScreenOrigin { get; } = new(10000, 10000);

    /// <summary>Scales size down to fit limitWidth x limitHeight with the aspect ratio kept
    /// and each dimension rounded half away from zero. The result is never larger than the
    /// input. When no scaling is needed, size is returned unchanged and not rounded.</summary>
    /// <param name="size">The size to fit.</param>
    /// <param name="limitWidth">Maximum width.</param>
    /// <param name="limitHeight">Maximum height.</param>
    /// <returns>The fitted size.</returns>
    public static Size FitWithin(Size size, double limitWidth, double limitHeight)
    {
        bool any = false;
        double s = 0;
        if (size.Width > DegenerateDimension)
        {
            s = limitWidth / size.Width;
            any = true;
        }
        if (size.Height > DegenerateDimension)
        {
            double r = limitHeight / size.Height;
            s = any ? Math.Min(s, r) : r;
            any = true;
        }
        if (!any) return size;
        if (s < 1)
        {
            return new Size(Math.Round(size.Width * s, MidpointRounding.AwayFromZero),
                            Math.Round(size.Height * s, MidpointRounding.AwayFromZero));
        }
        return size;
    }

    /// <summary>Park size: FitWithin to the work-area extent, then each dimension raised to
    /// Ceiling(minimum) when a minimum is given.</summary>
    /// <param name="size">The window size.</param>
    /// <param name="visibleArea">The work area; only Width and Height are read.</param>
    /// <param name="minimum">The window's minimum size, or null.</param>
    /// <returns>The park size.</returns>
    public static Size ParkSize(Size size, Rect visibleArea, Size? minimum)
    {
        Size f = FitWithin(size, visibleArea.Width, visibleArea.Height);
        if (minimum is { } m)
        {
            return new Size(Math.Max(f.Width, Math.Ceiling(m.Width)),
                            Math.Max(f.Height, Math.Ceiling(m.Height)));
        }
        return f;
    }

    /// <summary>Park-small size: fits one SmallDivisor-th of the work area per axis; no minimum.</summary>
    /// <param name="size">The window size.</param>
    /// <param name="visibleArea">The work area.</param>
    /// <param name="options">Options, or null for defaults.</param>
    /// <returns>The park-small size.</returns>
    public static Size ParkSmallSize(Size size, Rect visibleArea, ParkOptions? options = null)
    {
        // SPEC-ASSUMPTION A11: the divisor applies per axis to the work area.
        double d = (options ?? ParkOptions.Default).SmallDivisor;
        return FitWithin(size, visibleArea.Width / d, visibleArea.Height / d);
    }

    /// <summary>The five candidates around displayFrame for a window of parkSize, in fixed order.</summary>
    /// <param name="displayFrame">The display's full frame.</param>
    /// <param name="parkSize">The window's park size.</param>
    /// <param name="options">Options, or null for defaults.</param>
    /// <returns>Exactly five candidates in a new read-only list.</returns>
    public static IReadOnlyList<ParkCandidate> Candidates(Rect displayFrame, Size parkSize,
                                                         ParkOptions? options = null)
    {
        ParkOptions opt = options ?? ParkOptions.Default;
        double o = opt.EdgeOverlap, r = opt.Reach;
        Rect d = displayFrame;
        double w = parkSize.Width, h = parkSize.Height;
        double maxX = d.X + d.Width, maxY = d.Y + d.Height;
        return Array.AsReadOnly(new[]
        {
            new ParkCandidate(new Rect(maxX - o, maxY - o, w, h), new Rect(maxX - o, maxY - r, w, h + r)),
            new ParkCandidate(new Rect(d.X - w + o, maxY - o, w, h), new Rect(d.X - w + o, maxY - r, w, h + r)),
            new ParkCandidate(new Rect(maxX - o, d.Y, w, h), new Rect(maxX - o, d.Y, w, h + r)),
            new ParkCandidate(new Rect(d.X - w + o, d.Y, w, h), new Rect(d.X - w + o, d.Y, w, h + r)),
            new ParkCandidate(new Rect(d.X, maxY - o, w, h), new Rect(d.X, maxY - r, w, h + r)),
        });
    }

    /// <summary>Area of a intersect b. Returns 0 when the intersection is empty or only touches.</summary>
    /// <param name="a">First rect.</param>
    /// <param name="b">Second rect.</param>
    /// <returns>The intersection area.</returns>
    public static double IntersectionArea(Rect a, Rect b)
    {
        double w = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        double h = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? w * h : 0.0;
    }

    /// <summary>Sum of IntersectionArea(reach, d) over others, in list order, starting from 0.0.</summary>
    /// <param name="reach">The reach rect.</param>
    /// <param name="others">The other displays.</param>
    /// <returns>The score.</returns>
    /// <exception cref="ArgumentNullException">others is null.</exception>
    public static double Score(Rect reach, IReadOnlyList<Rect> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        double sum = 0.0;
        for (int i = 0; i < others.Count; i++) sum += IntersectionArea(reach, others[i]);
        return sum;
    }

    /// <summary>Index of the winning candidate: the first with score exactly 0, otherwise the
    /// lowest score, ties going to the earlier candidate.</summary>
    /// <param name="candidates">The candidates.</param>
    /// <param name="others">The other displays.</param>
    /// <returns>The winning index.</returns>
    /// <exception cref="ArgumentNullException">candidates or others is null.</exception>
    /// <exception cref="ArgumentException">candidates is empty.</exception>
    public static int ChooseCandidate(IReadOnlyList<ParkCandidate> candidates,
                                      IReadOnlyList<Rect> others)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(others);
        if (candidates.Count == 0) throw new ArgumentException("Candidates must not be empty.", nameof(candidates));
        int best = -1;
        double bestScore = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            double s = Score(candidates[i].Reach, others);
            if (s == 0.0) return i;
            if (best < 0 || s < bestScore)
            {
                best = i;
                bestScore = s;
            }
        }
        return best;
    }

    /// <summary>The park frame for a window of the given size. Positions are not rounded.</summary>
    /// <param name="display">The display context.</param>
    /// <param name="size">The window size.</param>
    /// <param name="minimum">The window's minimum size, or null for none.</param>
    /// <param name="options">Options, or null for defaults.</param>
    /// <returns>The park frame.</returns>
    /// <exception cref="ArgumentNullException">display is null.</exception>
    public static Rect ParkFrame(ParkDisplay display, Size size, Size? minimum,
                                 ParkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        Size p = ParkSize(size, display.VisibleArea, minimum);
        if (display.DisplayFrame is not { } frame)
        {
            return new Rect(display.FallbackOrigin.X, display.FallbackOrigin.Y, p.Width, p.Height);
        }
        IReadOnlyList<ParkCandidate> c = Candidates(frame, p, options);
        return c[ChooseCandidate(c, display.OtherDisplays)].Spot;
    }

    /// <summary>The park-small frame: ParkFrame with the park-small size and no minimum.</summary>
    /// <param name="display">The display context.</param>
    /// <param name="size">The window size.</param>
    /// <param name="options">Options, or null for defaults.</param>
    /// <returns>The park-small frame.</returns>
    /// <exception cref="ArgumentNullException">display is null.</exception>
    public static Rect ParkSmallFrame(ParkDisplay display, Size size, ParkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        return ParkFrame(display, ParkSmallSize(size, display.VisibleArea, options), null, options);
    }

    /// <summary>True when frame is exactly a park frame for its own size against the current
    /// display, with no minimum or with its own size as the minimum.</summary>
    /// <param name="display">The display context.</param>
    /// <param name="frame">The frame to test.</param>
    /// <param name="options">Options, or null for defaults.</param>
    /// <returns>Whether frame is a park frame.</returns>
    /// <exception cref="ArgumentNullException">display is null.</exception>
    public static bool IsParkFrame(ParkDisplay display, Rect frame, ParkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        var s = new Size(frame.Width, frame.Height);
        return SameFrame(ParkFrame(display, s, null, options), frame)
            || SameFrame(ParkFrame(display, s, s, options), frame);
    }

    private static bool SameFrame(Rect a, Rect b) =>
        a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;

    /// <summary>True when a window whose target was a park frame can be accepted as resting there.</summary>
    /// <param name="actual">The window's frame as reported by the OS.</param>
    /// <param name="target">The park frame that was written.</param>
    /// <param name="actualVisibleFraction">The visible fraction of actual.</param>
    /// <param name="options">Options, or null for defaults.</param>
    /// <returns>Whether the window rests parked.</returns>
    public static bool RestsParked(Rect actual, Rect target, double actualVisibleFraction,
                                   ParkOptions? options = null)
    {
        ParkOptions opt = options ?? ParkOptions.Default;
        double t = opt.Tolerance, r = opt.Reach;
        // SPEC-ASSUMPTION A7: NaN-safe form of the size gate.
        if (!(Math.Abs(actual.Width - target.Width) < t && Math.Abs(actual.Height - target.Height) < t))
            return false;
        if (actualVisibleFraction < opt.VisibleFractionThreshold) return true;
        return Math.Abs(actual.X - target.X) < t
            && actual.Y >= target.Y - (r + t)
            && actual.Y <= target.Y + t;
    }

    /// <summary>Sum of IntersectionArea(rect, s) over screens divided by rect area; 0 when
    /// Width or Height is at most 0.</summary>
    /// <param name="rect">The rect.</param>
    /// <param name="screens">The screens.</param>
    /// <returns>The visible fraction.</returns>
    /// <exception cref="ArgumentNullException">screens is null.</exception>
    public static double VisibleFraction(Rect rect, IReadOnlyList<Rect> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (!(rect.Width > 0 && rect.Height > 0)) return 0.0;
        // SPEC-ASSUMPTION A6: overlapping screens are counted once each (no de-duplication).
        double sum = 0.0;
        for (int i = 0; i < screens.Count; i++) sum += IntersectionArea(rect, screens[i]);
        return sum / (rect.Width * rect.Height);
    }

    /// <summary>A default fallback origin for adapters: the bottom-right corner of the screen
    /// with the largest right edge, moved inward by EdgeOverlap; NoScreenOrigin when empty.</summary>
    /// <param name="screens">The screens.</param>
    /// <param name="options">Options, or null for defaults.</param>
    /// <returns>The origin.</returns>
    /// <exception cref="ArgumentNullException">screens is null.</exception>
    public static Point DefaultFallbackOrigin(IReadOnlyList<Rect> screens, ParkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(screens);
        // SPEC-ASSUMPTION A5: DefaultFallbackOrigin is provided as a pure Core helper.
        if (screens.Count == 0) return NoScreenOrigin;
        double o = (options ?? ParkOptions.Default).EdgeOverlap;
        Rect best = screens[0];
        for (int i = 1; i < screens.Count; i++)
        {
            if (screens[i].X + screens[i].Width > best.X + best.Width) best = screens[i];
        }
        return new Point(best.X + best.Width - o, best.Y + best.Height - o);
    }
}
