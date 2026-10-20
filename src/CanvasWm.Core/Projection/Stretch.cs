using System.Collections.Immutable;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Projection;

/// <summary>Per-axis piecewise-linear maps of unit space. Immutable.
/// Equality: structural (breakpoint lists compared element-wise).</summary>
public sealed record Stretch
{
    /// <summary>No breakpoints on either axis: the identity map.</summary>
    public static Stretch Identity { get; } = new(Array.Empty<StretchBreakpoint>(), Array.Empty<StretchBreakpoint>());

    /// <summary>Creates a stretch.</summary>
    /// <param name="x">X-axis breakpoints.</param>
    /// <param name="y">Y-axis breakpoints.</param>
    /// <exception cref="ArgumentNullException">x or y is null.</exception>
    /// <exception cref="ArgumentException">a list has a non-finite value, or its From values
    /// are not strictly increasing.</exception>
    public Stretch(IEnumerable<StretchBreakpoint> x, IEnumerable<StretchBreakpoint> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        X = Validate(x, nameof(x));
        Y = Validate(y, nameof(y));
    }

    private static ImmutableArray<StretchBreakpoint> Validate(IEnumerable<StretchBreakpoint> list, string name)
    {
        ImmutableArray<StretchBreakpoint> a = list.ToImmutableArray();
        for (int i = 0; i < a.Length; i++)
        {
            if (!double.IsFinite(a[i].From) || !double.IsFinite(a[i].To))
            {
                throw new ArgumentException("Breakpoint values must be finite.", name);
            }
            // SPEC-ASSUMPTION A2: From values must be strictly increasing (throw otherwise).
            if (i > 0 && !(a[i].From > a[i - 1].From))
            {
                throw new ArgumentException("Breakpoint From values must be strictly increasing.", name);
            }
        }
        return a;
    }

    /// <summary>X-axis breakpoints.</summary>
    public ImmutableArray<StretchBreakpoint> X { get; }

    /// <summary>Y-axis breakpoints.</summary>
    public ImmutableArray<StretchBreakpoint> Y { get; }

    /// <summary>Maps a unit x value through X.</summary>
    /// <param name="value">Unit value.</param>
    public double MapX(double value) => Map(X, value);

    /// <summary>Maps a unit y value through Y.</summary>
    /// <param name="value">Unit value.</param>
    public double MapY(double value) => Map(Y, value);

    /// <summary>Piecewise-linear map through a breakpoint list. Empty list returns value.</summary>
    /// <param name="breakpoints">Breakpoints, From strictly increasing.</param>
    /// <param name="value">Unit value.</param>
    public static double Map(ImmutableArray<StretchBreakpoint> breakpoints, double value)
    {
        int n = breakpoints.IsDefault ? 0 : breakpoints.Length;
        if (n == 0)
        {
            return value;
        }
        // SPEC-ASSUMPTION A2: outside the breakpoints the map is a constant shift by the nearest
        // breakpoint's To - From.
        if (value <= breakpoints[0].From)
        {
            return value + (breakpoints[0].To - breakpoints[0].From);
        }
        if (value >= breakpoints[n - 1].From)
        {
            return value + (breakpoints[n - 1].To - breakpoints[n - 1].From);
        }
        for (int i = 0; i < n - 1; i++)
        {
            StretchBreakpoint a = breakpoints[i];
            StretchBreakpoint b = breakpoints[i + 1];
            if (a.From <= value && value < b.From)
            {
                return a.To + (value - a.From) * (b.To - a.To) / (b.From - a.From);
            }
        }
        return value;
    }

    /// <summary>The at-rest stretch that makes the framed rect square. Null, degenerate or
    /// already square returns Identity.</summary>
    /// <param name="framed">Unit rect of the framed node, or null.</param>
    public static Stretch ForFraming(Rect? framed)
    {
        // SPEC-ASSUMPTION A3: a degenerate framed rect behaves as "nothing framed".
        if (framed is not { } f || !IsValid(f) || Math.Abs(f.Width - f.Height) <= ProjectionView.SquareTolerance)
        {
            return Identity;
        }
        double maxX = f.X + f.Width;
        double maxY = f.Y + f.Height;
        if (f.Width < f.Height)
        {
            return new Stretch(
                new[] { new StretchBreakpoint(f.X, f.X), new StretchBreakpoint(maxX, f.X + f.Height) },
                Array.Empty<StretchBreakpoint>());
        }
        return new Stretch(
            Array.Empty<StretchBreakpoint>(),
            new[] { new StretchBreakpoint(f.Y, f.Y), new StretchBreakpoint(maxY, f.Y + f.Width) });
    }

    internal static bool IsValid(Rect f) =>
        double.IsFinite(f.X) && double.IsFinite(f.Y) && double.IsFinite(f.Width) && double.IsFinite(f.Height)
        && f.Width > 0 && f.Height > 0;

    /// <summary>Structural equality.</summary>
    /// <param name="other">Other stretch.</param>
    public bool Equals(Stretch? other) =>
        other is not null && X.SequenceEqual(other.X) && Y.SequenceEqual(other.Y);

    /// <summary>Hash code consistent with structural equality.</summary>
    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (StretchBreakpoint b in X) { h.Add(b); }
        h.Add(-1);
        foreach (StretchBreakpoint b in Y) { h.Add(b); }
        return h.ToHashCode();
    }
}
