using CanvasWm.Core.Geometry;
using CanvasWm.Core.Parking;

namespace CanvasWm.Core.Projection;

/// <summary>Pure geometry helpers of the projection.</summary>
public static class ScreenProjection
{
    /// <summary>Fraction of a frame that must be inside the stage: 0.995.</summary>
    public const double OnStageThreshold = 0.995;

    /// <summary>Floating on-stage fraction (strictly greater) when the desktop slot is usable: 0.05.</summary>
    public const double FloatingOnStageThreshold = 0.05;

    /// <summary>Slack of the fits test, DIPs: 0.5.</summary>
    public const double FitsSlack = 0.5;

    /// <summary>Slack of the grow-to-minimum test, DIPs: 0.5.</summary>
    public const double GrowSlack = 0.5;

    /// <summary>Strip height as a fraction of the frame height: 0.15.</summary>
    public const double StripHeightFraction = 0.15;

    /// <summary>Frame width above which a strip side inset applies: 120.</summary>
    public const double StripInsetThreshold = 120;

    /// <summary>Frame extent at or below which the preview size falls back: 1.</summary>
    public const double DegeneratePreviewExtent = 1;

    /// <summary>(800, 600): park/preview size when nothing better is known.</summary>
    // SPEC-ASSUMPTION A5: unknown last frame size falls back to 800 x 600.
    public static Size FallbackWindowSize { get; } = new(800, 600);

    internal static double ClampGap(double gap) => double.IsFinite(gap) && gap >= 0 ? gap : 0;

    /// <summary>The stage: work area inset by half the gap on every side.</summary>
    /// <param name="workArea">Work area.</param>
    /// <param name="gap">Gap; NaN, infinity or negative is treated as 0.</param>
    public static Rect Stage(Rect workArea, double gap)
    {
        double g = ClampGap(gap);
        return new Rect(workArea.X + g / 2, workArea.Y + g / 2,
                        Math.Max(0, workArea.Width - g), Math.Max(0, workArea.Height - g));
    }

    /// <summary>At-rest camera: the framed rect made square, or (0, 0, 1, 1).</summary>
    /// <param name="framed">Framed unit rect or null.</param>
    public static Rect CameraForFraming(Rect? framed)
    {
        // SPEC-ASSUMPTION A3: a degenerate framed rect behaves as "nothing framed".
        if (framed is not { } f || !Stretch.IsValid(f))
        {
            return new Rect(0, 0, 1, 1);
        }
        double s = Math.Max(f.Width, f.Height);
        return new Rect(f.X, f.Y, s, s);
    }

    /// <summary>Un-inset projected rect of a unit rect.</summary>
    /// <param name="view">The view.</param>
    /// <param name="unit">Unit rect.</param>
    public static Rect Project(ProjectionView view, Rect unit)
    {
        ArgumentNullException.ThrowIfNull(view);
        Rect st = view.Stage;
        Rect c = view.Camera;
        Stretch s = view.Stretch;
        double x0 = s.MapX(unit.X);
        double x1 = s.MapX(unit.X + unit.Width);
        double y0 = s.MapY(unit.Y);
        double y1 = s.MapY(unit.Y + unit.Height);
        return new Rect(st.X + st.Width * (x0 - c.X) / c.Width,
                        st.Y + st.Height * (y0 - c.Y) / c.Height,
                        st.Width * (x1 - x0) / c.Width,
                        st.Height * (y1 - y0) / c.Height);
    }

    /// <summary>Project, then inset by Gap/2 on each side.</summary>
    /// <param name="view">The view.</param>
    /// <param name="unit">Unit rect.</param>
    public static Rect DockedFrame(ProjectionView view, Rect unit)
    {
        ArgumentNullException.ThrowIfNull(view);
        return InsetHalfGap(Project(view, unit), view.Gap);
    }

    /// <summary>Rect inset by gap/2 on each side, extents clamped at 0.</summary>
    /// <param name="rect">Rect.</param>
    /// <param name="gap">Gap, clamped like the view gap.</param>
    public static Rect InsetHalfGap(Rect rect, double gap)
    {
        double g = ClampGap(gap);
        return new Rect(rect.X + g / 2, rect.Y + g / 2,
                        Math.Max(0, rect.Width - g), Math.Max(0, rect.Height - g));
    }

    /// <summary>Aspect fit to a natural size. Invalid natural or empty rect returns rect.</summary>
    /// <param name="rect">Rect to fit.</param>
    /// <param name="natural">Natural size.</param>
    public static Rect FitAspect(Rect rect, Size natural)
    {
        if (!double.IsFinite(natural.Width) || !double.IsFinite(natural.Height)
            || !(natural.Width > 0) || !(natural.Height > 0)
            || !(rect.Width > 0) || !(rect.Height > 0))
        {
            return rect;
        }
        double a = natural.Width / natural.Height;
        if (a < rect.Width / rect.Height)
        {
            double w = a * rect.Height;
            return new Rect(rect.X + (rect.Width - w) / 2, rect.Y, w, rect.Height);
        }
        return new Rect(rect.X, rect.Y, rect.Width, rect.Width / a);
    }

    /// <summary>Grow-to-minimum: scales a too-small frame up uniformly, keeping it in the stage.</summary>
    /// <param name="frame">Frame.</param>
    /// <param name="minimum">Minimum size.</param>
    /// <param name="stage">Stage.</param>
    public static Rect GrowToMinimum(Rect frame, Size minimum, Rect stage)
    {
        if ((frame.Width < minimum.Width - GrowSlack || frame.Height < minimum.Height - GrowSlack)
            && frame.Width > 0 && frame.Height > 0)
        {
            double k = Math.Max(minimum.Width / frame.Width, minimum.Height / frame.Height);
            k = Math.Min(k, Math.Min(stage.Width / frame.Width, stage.Height / frame.Height));
            if (k > 1)
            {
                double w = frame.Width * k;
                double h = frame.Height * k;
                double x = frame.X + frame.Width / 2 - w / 2;
                double y = frame.Y;
                // SPEC-ASSUMPTION A11: the grown float is kept inside the stage.
                x = Math.Max(stage.X, Math.Min(x, stage.X + stage.Width - w));
                y = Math.Max(stage.Y, Math.Min(y, stage.Y + stage.Height - h));
                return new Rect(x, y, w, h);
            }
        }
        return frame;
    }

    /// <summary>area(frame intersect stage) / area(frame); 0 for zero, negative or non-finite area.</summary>
    /// <param name="frame">Frame.</param>
    /// <param name="stage">Stage.</param>
    public static double OnStageFraction(Rect frame, Rect stage)
    {
        if (!double.IsFinite(frame.Width) || !double.IsFinite(frame.Height)
            || !(frame.Width > 0) || !(frame.Height > 0))
        {
            return 0;
        }
        return ParkGeometry.IntersectionArea(frame, stage) / (frame.Width * frame.Height);
    }

    /// <summary>Effective minimum: per axis max(floor, own).</summary>
    /// <param name="fixedMinimum">Use the fixed minimum instead of the floor.</param>
    /// <param name="own">The window's own minimum, if any.</param>
    /// <param name="tuning">Tuning constants.</param>
    public static Size EffectiveMinimum(bool fixedMinimum, Size? own, Tuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        Size floor = fixedMinimum ? tuning.FixedTileMinimum : tuning.TileMinimumFloor;
        double ow = 0, oh = 0;
        if (own is { } o)
        {
            ow = double.IsFinite(o.Width) && o.Width > 0 ? o.Width : 0;
            oh = double.IsFinite(o.Height) && o.Height > 0 ? o.Height : 0;
        }
        return new Size(Math.Max(floor.Width, ow), Math.Max(floor.Height, oh));
    }

    /// <summary>frame.w at least m.w - 0.5 and frame.h at least m.h - 0.5.</summary>
    /// <param name="frame">Frame.</param>
    /// <param name="minimum">Minimum.</param>
    public static bool Fits(Rect frame, Size minimum) =>
        frame.Width >= minimum.Width - FitsSlack && frame.Height >= minimum.Height - FitsSlack;

    /// <summary>Maximum clamp, centred on both axes. Null maximum returns frame.</summary>
    /// <param name="frame">Frame.</param>
    /// <param name="maximum">Maximum size or null.</param>
    public static Rect ClampToMaximum(Rect frame, Size? maximum)
    {
        if (maximum is not { } m)
        {
            return frame;
        }
        double x = frame.X, y = frame.Y, w = frame.Width, h = frame.Height;
        // SPEC-ASSUMPTION A12: non-positive or non-finite maximum components are ignored.
        if (double.IsFinite(m.Width) && m.Width > 0 && m.Width < w)
        {
            x += (w - m.Width) / 2;
            w = m.Width;
        }
        if (double.IsFinite(m.Height) && m.Height > 0 && m.Height < h)
        {
            y += (h - m.Height) / 2;
            h = m.Height;
        }
        return new Rect(x, y, w, h);
    }

    /// <summary>Rounds each edge to whole DIPs (halves away from zero); extents never negative.</summary>
    /// <param name="rect">Rect.</param>
    public static Rect RoundEdges(Rect rect)
    {
        double x = Math.Round(rect.X, MidpointRounding.AwayFromZero);
        double y = Math.Round(rect.Y, MidpointRounding.AwayFromZero);
        double w = Math.Max(0, Math.Round(rect.X + rect.Width, MidpointRounding.AwayFromZero) - x);
        double h = Math.Max(0, Math.Round(rect.Y + rect.Height, MidpointRounding.AwayFromZero) - y);
        return new Rect(x, y, w, h);
    }

    /// <summary>Preview (scaled-up) park size.</summary>
    /// <param name="frame">The tile frame.</param>
    /// <param name="minimum">Effective minimum.</param>
    /// <param name="lastFrameSize">Last frame size, if known.</param>
    public static Size PreviewSize(Rect frame, Size minimum, Size? lastFrameSize)
    {
        if (!double.IsFinite(frame.Width) || !double.IsFinite(frame.Height)
            || frame.Width <= DegeneratePreviewExtent || frame.Height <= DegeneratePreviewExtent)
        {
            return lastFrameSize ?? FallbackWindowSize;
        }
        double k = Math.Max(1, Math.Max(minimum.Width / frame.Width, minimum.Height / frame.Height));
        return new Size(Math.Round(frame.Width * k, MidpointRounding.AwayFromZero),
                        Math.Round(frame.Height * k, MidpointRounding.AwayFromZero));
    }

    /// <summary>2 * tp + tuning.TabStripContentHeight, tp = TabPadding clamped to at least 0.</summary>
    /// <param name="tabPadding">Tab padding.</param>
    /// <param name="tuning">Tuning constants.</param>
    public static double StripFullHeight(double tabPadding, Tuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        double tp = double.IsFinite(tabPadding) && tabPadding >= 0 ? tabPadding : 0;
        return 2 * tp + tuning.TabStripContentHeight;
    }

    /// <summary>Strip rect at rest (reveal 1) for a frame.</summary>
    /// <param name="frame">Placement frame.</param>
    /// <param name="fullHeight">Full strip height.</param>
    /// <param name="tabInset">Largest side inset.</param>
    public static Rect StripRect(Rect frame, double fullHeight, double tabInset)
    {
        double s = Math.Min(StripHeightFraction * frame.Height, fullHeight);
        double i = 0;
        if (double.IsFinite(tabInset) && tabInset > 0)
        {
            i = Math.Max(0, Math.Min((frame.Width - StripInsetThreshold) / 2, tabInset));
        }
        return new Rect(frame.X + i, frame.Y, frame.Width - 2 * i, s);
    }
}
