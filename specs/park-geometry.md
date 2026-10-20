# Spec: park-geometry

## Goal

Pure geometry for **parking** a window. Parking moves a real window out of
sight without hiding or minimising it. The window is moved so that it hangs
off the edge of its own display and overlaps that display by a thin sliver.
The spot is chosen so that the window does not land on any *other* display.
This feature computes:

1. a **park size**: the window size scaled down to fit the display's work
   area with the aspect ratio kept and whole-unit rounding, never enlarged,
   then raised to the window's minimum. There is also a **park-small** size
   that fits one third of the work area in each direction and has no minimum;
2. five **position candidates** around the display's full frame, in a fixed
   order. Each candidate is scored by how much of its vertically extended
   "reach" rect overlaps the other displays. The first candidate with score
   zero wins; otherwise the lowest score wins and ties go to the earlier
   candidate;
3. a **fallback** frame at a caller-supplied origin when the display frame is
   unknown;
4. three **recognisers and measures**: *is this exactly a park frame?*, *has
   this window come to rest at its park spot?*, and *what fraction of a rect
   is on any screen?*

The constants were tuned for macOS: sliver 1, reach 60, tolerance 1,
visible-fraction threshold 0.05 and small divisor 3. They are parameters in
an immutable options record, and the observed values are the defaults.
Feature #14 (win32-parking) is expected to tune them for Windows.

Out of scope:
- Engine (#13 / #14): which windows are parked and when, which size path each
  one uses (plain, park-small, floor or preview scaling), the "parked small"
  key set, restoring the size on un-park, handling a refused park (the
  `checkPlacement` reaction), `parkSettle`, and the adoption-time "moved by
  user" predicate.
- #14 win32-parking: OS moves, DWM extended-frame-bounds handling, the
  crash-safe restore journal, and real Windows values for the options.
- Overview-mode parking.

## Placement

- Project: `src/CanvasWm.Core` (`net10.0`, BCL only; no new package or project
  references). No I/O and no OS access.
- Namespaces:
  - `CanvasWm.Core.Parking` (new): `ParkOptions`, `ParkDisplay`,
    `ParkCandidate`, `ParkGeometry`.
  - `CanvasWm.Core.Geometry`: one **new** value type, `Point` (A1).
- Suggested files: `src/CanvasWm.Core/Geometry/Point.cs`,
  `src/CanvasWm.Core/Parking/ParkOptions.cs`,
  `src/CanvasWm.Core/Parking/ParkDisplay.cs`,
  `src/CanvasWm.Core/Parking/ParkCandidate.cs`,
  `src/CanvasWm.Core/Parking/ParkGeometry.cs`.
- Tests: `tests/CanvasWm.Core.Tests`, suggested files
  `Parking/ParkSizeTests.cs`, `Parking/ParkCandidateTests.cs`,
  `Parking/ParkFrameTests.cs`, `Parking/ParkRecogniserTests.cs`,
  `Parking/ParkOptionsTests.cs`, `Parking/ParkGeometryPropertyTests.cs`.
- Depends on:
  - `specs/layout-tree.md` (A9): `Rect(X, Y, Width, Height)` and
    `Size(Width, Height)` in `CanvasWm.Core.Geometry`. These are positional
    `readonly record struct`s with no members. **Neither is changed.** Derived
    quantities such as `MaxX = X + Width` are computed inside `ParkGeometry`
    (A2).
  - No dependency on layout, solver, docking, placement or navigation.
- Units: device-independent units (DIPs), y-down, origin at the top-left of
  the virtual desktop (CLAUDE.md). The algorithm uses only relative
  positions, so it does not depend on where the origin is.

## Public API

All types are public and documented with XML docs. A reference argument that
is not marked nullable throws `ArgumentNullException` when it is null. The
functions do not validate geometry (sizes, rect extents, NaN). They apply the
formulas as written and never throw for non-null arguments, except where an
exception is listed.

```csharp
namespace CanvasWm.Core.Geometry;

/// <summary>A point in device-independent units, y-down.</summary>
/// <param name="X">Horizontal coordinate.</param>
/// <param name="Y">Vertical coordinate.</param>
public readonly record struct Point(double X, double Y);
```

```csharp
namespace CanvasWm.Core.Parking;

using CanvasWm.Core.Geometry;

/// <summary>Tunable constants of park geometry. The defaults are the values observed in the
/// studied app on macOS. Feature #14 may choose different values for Windows.
/// Compiler-generated value equality.</summary>
public sealed record ParkOptions
{
    /// <summary>Default options: EdgeOverlap 1, Reach 60, Tolerance 1,
    /// VisibleFractionThreshold 0.05, SmallDivisor 3.</summary>
    public static ParkOptions Default { get; } = new();

    /// <summary>How far a park spot overlaps its own display: the corner square side, the
    /// column width or the row height. Default 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is negative, NaN or infinite.</exception>
    public double EdgeOverlap { get; init; } = 1.0;

    /// <summary>Vertical extension of a candidate's reach rect, used for scoring (B10, B11) and
    /// as the upward slack in RestsParked (B21). Default 60.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is negative, NaN or infinite.</exception>
    public double Reach { get; init; } = 60.0;

    /// <summary>Size and position tolerance of RestsParked (B19, B21). Default 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is not greater than 0, or is NaN or
    /// infinite.</exception>
    public double Tolerance { get; init; } = 1.0;

    /// <summary>A rect whose visible fraction is strictly below this value counts as off screen
    /// (B20). Default 0.05.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is outside [0, 1] or is NaN.</exception>
    public double VisibleFractionThreshold { get; init; } = 0.05;

    /// <summary>Park-small fits the window into (work-area width / SmallDivisor,
    /// work-area height / SmallDivisor) (B6). Default 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is less than 1, or is NaN or
    /// infinite.</exception>
    public double SmallDivisor { get; init; } = 3.0;
}

/// <summary>The display context of a park computation. All rects are in the shared y-down DIP
/// space.</summary>
public sealed record ParkDisplay
{
    /// <param name="visibleArea">The display's work area. Only its Width and Height are used.</param>
    /// <param name="displayFrame">The display's full frame, or null when it is unknown (B13).</param>
    /// <param name="otherDisplays">Rects of the other displays. The display's own frame is not
    /// included. The constructor copies the sequence (B15, A4).</param>
    /// <param name="fallbackOrigin">Origin of the park frame when displayFrame is null.</param>
    /// <exception cref="ArgumentNullException">otherDisplays is null.</exception>
    public ParkDisplay(Rect visibleArea, Rect? displayFrame,
                       IEnumerable<Rect> otherDisplays, Point fallbackOrigin);

    public Rect VisibleArea { get; }
    public Rect? DisplayFrame { get; }
    /// <summary>An immutable snapshot in the order given.</summary>
    public IReadOnlyList<Rect> OtherDisplays { get; }
    public Point FallbackOrigin { get; }
}
// Equality: compiler-generated record equality. OtherDisplays is compared by reference, so
// two separately built ParkDisplay instances are not Equal even when their contents match.
// Tests must not rely on value equality of ParkDisplay.

/// <summary>One park position. Spot is the frame the window is moved to. Reach is the spot
/// extended vertically and is used only for scoring.</summary>
public readonly record struct ParkCandidate(Rect Spot, Rect Reach);

/// <summary>Pure park geometry (findings rules 1-19).</summary>
public static class ParkGeometry
{
    /// <summary>Window dimensions at or below this value (1) are not used as a divisor in
    /// FitWithin (B3).</summary>
    public const double DegenerateDimension = 1.0;

    /// <summary>Origin returned by DefaultFallbackOrigin when there are no screens:
    /// (10000, 10000).</summary>
    public static Point NoScreenOrigin { get; }

    /// <summary>Scales size down to fit limitWidth x limitHeight with the aspect ratio kept
    /// and each dimension rounded half away from zero. The result is never larger than the
    /// input. When no scaling is needed, size is returned unchanged and not rounded
    /// (B1-B4).</summary>
    public static Size FitWithin(Size size, double limitWidth, double limitHeight);

    /// <summary>Park size: FitWithin(size, visibleArea.Width, visibleArea.Height), then each
    /// dimension raised to Ceiling(minimum) when a minimum is given (B5).</summary>
    public static Size ParkSize(Size size, Rect visibleArea, Size? minimum);

    /// <summary>Park-small size: FitWithin(size, visibleArea.Width / d,
    /// visibleArea.Height / d) with d = options.SmallDivisor. No minimum is applied (B6).</summary>
    public static Size ParkSmallSize(Size size, Rect visibleArea, ParkOptions? options = null);

    /// <summary>The five candidates around displayFrame for a window of parkSize, in the
    /// fixed order of B8. Always returns 5 elements.</summary>
    public static IReadOnlyList<ParkCandidate> Candidates(Rect displayFrame, Size parkSize,
                                                         ParkOptions? options = null);

    /// <summary>Area of a ∩ b. Returns 0 when the intersection is empty or only touches
    /// (B11).</summary>
    public static double IntersectionArea(Rect a, Rect b);

    /// <summary>Sum of IntersectionArea(reach, d) over others, in list order, starting from
    /// 0.0 (B11).</summary>
    /// <exception cref="ArgumentNullException">others is null.</exception>
    public static double Score(Rect reach, IReadOnlyList<Rect> others);

    /// <summary>Index of the winning candidate. This is the first candidate whose score is
    /// exactly 0. If no score is 0, it is the lowest score, and ties go to the earlier
    /// candidate (B12).</summary>
    /// <exception cref="ArgumentNullException">candidates or others is null.</exception>
    /// <exception cref="ArgumentException">candidates is empty.</exception>
    public static int ChooseCandidate(IReadOnlyList<ParkCandidate> candidates,
                                      IReadOnlyList<Rect> others);

    /// <summary>The park frame for a window of the given size (B5, B8-B14). Positions are not
    /// rounded.</summary>
    /// <param name="minimum">The window's minimum size, or null for none.</param>
    /// <exception cref="ArgumentNullException">display is null.</exception>
    public static Rect ParkFrame(ParkDisplay display, Size size, Size? minimum,
                                 ParkOptions? options = null);

    /// <summary>The park-small frame: ParkFrame(display, ParkSmallSize(size, display.VisibleArea,
    /// options), minimum: null, options) (B6, B7).</summary>
    /// <exception cref="ArgumentNullException">display is null.</exception>
    public static Rect ParkSmallFrame(ParkDisplay display, Size size, ParkOptions? options = null);

    /// <summary>True when frame is exactly a park frame for its own size against the current
    /// display, with no minimum or with its own size as the minimum (B16-B18).</summary>
    /// <exception cref="ArgumentNullException">display is null.</exception>
    public static bool IsParkFrame(ParkDisplay display, Rect frame, ParkOptions? options = null);

    /// <summary>True when a window whose target was a park frame can be accepted as resting
    /// there (B19-B22).</summary>
    /// <param name="actual">The window's frame as reported by the OS.</param>
    /// <param name="target">The park frame that was written.</param>
    /// <param name="actualVisibleFraction">The visible fraction of actual, normally
    /// VisibleFraction(actual, screens).</param>
    public static bool RestsParked(Rect actual, Rect target, double actualVisibleFraction,
                                   ParkOptions? options = null);

    /// <summary>Sum of IntersectionArea(rect, s) over screens, divided by
    /// rect.Width * rect.Height. Returns 0 when Width or Height is at most 0 (B23, B24).</summary>
    /// <exception cref="ArgumentNullException">screens is null.</exception>
    public static double VisibleFraction(Rect rect, IReadOnlyList<Rect> screens);

    /// <summary>A default fallback origin for adapters: the bottom-right corner of the screen
    /// with the largest right edge, moved inward by EdgeOverlap. Returns NoScreenOrigin when
    /// screens is empty (B25, A5).</summary>
    /// <exception cref="ArgumentNullException">screens is null.</exception>
    public static Point DefaultFallbackOrigin(IReadOnlyList<Rect> screens,
                                              ParkOptions? options = null);
}
```

`options: null` always means `ParkOptions.Default`.

## Behaviour

Notation: for a rect `R`, `R.MaxX = R.X + R.Width` and
`R.MaxY = R.Y + R.Height`. "Round" means
`Math.Round(v, MidpointRounding.AwayFromZero)`. "Ceil" means `Math.Ceiling`.
`o = EdgeOverlap`, `r = Reach`, `t = Tolerance`.

### Fit and park size
- **B1 (fit scale).** `FitWithin(size, lw, lh)`: list the *used* axes. The
  width axis is used when `size.Width > 1`, and its ratio is
  `lw / size.Width`. The height axis is used when `size.Height > 1`, and its
  ratio is `lh / size.Height`. `s` is the minimum ratio over the used axes.
  If no axis is used, the result is `size`.
- **B2 (shrink only).** If `s < 1`, the result is
  `(Round(size.Width * s), Round(size.Height * s))`. **Both** dimensions are
  scaled, including an axis that was not used. Otherwise (`s >= 1`, or `s` is
  NaN) the result is `size`, bit-for-bit unchanged and not rounded. The size
  is never enlarged.
- **B3 (degenerate axis).** A dimension `<= DegenerateDimension` (1) never
  limits the scale (B1). A 1-unit-wide window is constrained only by its
  height.
- **B4 (sizes only).** Rounding applies only to sizes. Park geometry never
  rounds a position.
- **B5 (park size).** `ParkSize(size, visibleArea, minimum)` starts with
  `f = FitWithin(size, visibleArea.Width, visibleArea.Height)`. When
  `minimum` is non-null, the result is
  `(Math.Max(f.Width, Ceil(minimum.Width)), Math.Max(f.Height, Ceil(minimum.Height)))`.
  Otherwise the result is `f`. The minimum is applied after the fit, so the
  result may be larger than the work area. Only `visibleArea.Width` and
  `visibleArea.Height` are read.
- **B6 (park-small size).** `ParkSmallSize(size, visibleArea, options)` is
  `FitWithin(size, visibleArea.Width / d, visibleArea.Height / d)` with
  `d = options.SmallDivisor`. It applies no minimum.
- **B7 (park-small frame).** `ParkSmallFrame(display, size, options)` equals
  `ParkFrame(display, ParkSmallSize(size, display.VisibleArea, options), null, options)`.
  Because that inner size already fits, `ParkFrame` does not change it again
  (the inner `FitWithin` returns it unchanged, by B2).

### Candidates (display frame present)
- **B8 (five candidates, fixed order).** `Candidates(D, (W, H), options)`
  returns, in this order:

  | # | where | Spot (X, Y, Width, Height) | Reach (X, Y, Width, Height) |
  |---|---|---|---|
  | 0 | below-right corner | (D.MaxX − o, D.MaxY − o, W, H) | (D.MaxX − o, D.MaxY − r, W, H + r) |
  | 1 | below-left corner | (D.X − W + o, D.MaxY − o, W, H) | (D.X − W + o, D.MaxY − r, W, H + r) |
  | 2 | right side, top-aligned | (D.MaxX − o, D.Y, W, H) | (D.MaxX − o, D.Y, W, H + r) |
  | 3 | left side, top-aligned | (D.X − W + o, D.Y, W, H) | (D.X − W + o, D.Y, W, H + r) |
  | 4 | below, left-aligned | (D.X, D.MaxY − o, W, H) | (D.X, D.MaxY − r, W, H + r) |

  The expressions are evaluated exactly as written, with no rounding. With
  `o = 1`, `r = 60`, the "below" reaches start 59 above the spot and end 1
  below it. The side reaches extend 60 below the spot.
- **B9 (own-display overlap).** When `W >= o`, `H >= o`, `D.Width >= W` and
  `D.Height >= H`, `Spot ∩ D` is an `o × o` square for candidates 0 and 1, an
  `o × H` column for 2 and 3, and a `W × o` row for 4. This is a consequence
  of B8 and is checked by the property test. It is not a separate rule to
  implement.
- **B10 (reach is for scoring only).** Every result returns a spot, never a
  reach.

### Scoring and choice
- **B11 (score).** `IntersectionArea(a, b)`: `w = min(a.MaxX, b.MaxX) − max(a.X, b.X)`
  and `h = min(a.MaxY, b.MaxY) − max(a.Y, b.Y)`. The result is `0.0` if
  `!(w > 0 && h > 0)`, otherwise `w * h`. Rects that only touch score 0.
  `Score(reach, others)` adds the areas in list order, starting from `0.0`.
- **B12 (choice).** `ChooseCandidate` visits the candidates in index order.
  For each one it computes `Score(candidate.Reach, others)`. It returns the
  first index whose score `== 0.0`. If no score is zero, it returns the index
  with the smallest score, compared with strict `<` so that ties keep the
  earlier index. If `others` is empty, every score is 0, so it returns 0. A
  NaN score never compares less, so a later NaN is never chosen over the
  current best. The search starts with index 0 as the best, so if index 0
  scores NaN, nothing compares less than it and index 0 is returned (unless a
  later score is exactly 0.0, which is returned first). In practice `Score`
  never returns NaN: a rect with a NaN component has no positive overlap and
  scores 0.0 (pinned by AC18), so the NaN branch is defensive only.
- **B13 (park frame).** `ParkFrame(display, size, minimum, options)`:
  1. `(W, H) = ParkSize(size, display.VisibleArea, minimum)`.
  2. If `display.DisplayFrame` is null, the result is
     `(display.FallbackOrigin.X, display.FallbackOrigin.Y, W, H)`. No
     candidates are built and nothing is scored.
  3. Otherwise `c = Candidates(display.DisplayFrame.Value, (W, H), options)`,
     and the result is `c[ChooseCandidate(c, display.OtherDisplays)].Spot`.
- **B14 (determinism).** Every function is a pure function of its arguments.
  It does not read the clock, randomness or global state, and it gives
  bit-identical results for equal inputs.
- **B15 (display snapshot).** `ParkDisplay` copies `otherDisplays` into an
  internal read-only array at construction. Later changes to the source
  collection do not affect it.

### Recognising a park frame
- **B16 (first test).** `s = (frame.Width, frame.Height)`. `IsParkFrame` is
  true if `p1 = ParkFrame(display, s, null, options)` equals `frame` in every
  component under the C# `==` operator on `double`. This is exact, with no
  tolerance.
- **B17 (second test).** Otherwise it is true if
  `p2 = ParkFrame(display, s, s, options)` equals `frame` under the same
  component-wise `==`. Otherwise it is false.
- **B18 (current displays only).** The check uses only the given `display`.
  A frame that was parked against an earlier display set is generally not
  recognised after the displays change. The function keeps no state.
  Component-wise `==` means a frame with any NaN component is never a park
  frame.

### Accepting a parked window as at rest
- **B19 (size gate).** `RestsParked` returns false unless
  `|actual.Width − target.Width| < t` **and**
  `|actual.Height − target.Height| < t`. This form is NaN-safe: a NaN
  difference gives false (A7).
- **B20 (mostly off screen).** If the size gate passes and
  `actualVisibleFraction < VisibleFractionThreshold` (strict), the result is
  true.
- **B21 (position gate).** Otherwise the result is true only when all of the
  following hold:
  `|actual.X − target.X| < t`,
  `actual.Y >= target.Y − (r + t)` and
  `actual.Y <= target.Y + t`. With the defaults the allowed Y band is
  `[target.Y − 61, target.Y + 1]`. In every other case the result is false.
- **B22 (no state).** `RestsParked` does not check that `target` is a park
  frame. That check, and the "refused park" decision (findings rule 20), are
  the Engine's job.

### Visible fraction
- **B23.** `VisibleFraction(rect, screens)` returns `0.0` when
  `!(rect.Width > 0 && rect.Height > 0)`. Otherwise it returns
  `(Σ IntersectionArea(rect, s) over screens in list order) / (rect.Width * rect.Height)`.
  It returns `0.0` when `screens` is empty.
- **B24 (no de-duplication).** Screens that overlap each other are counted
  once each, so the result can be greater than 1 (A6).

### Default fallback origin (adapter helper)
- **B25.** `DefaultFallbackOrigin(screens, options)`: if `screens` is empty,
  it returns `NoScreenOrigin = (10000, 10000)`. Otherwise it picks the screen
  with the largest `MaxX`, compared with strict `>` so that the first screen
  wins ties. It returns `(S.MaxX − o, S.MaxY − o)`. The Engine or the adapter
  decides whether to use it. `ParkFrame` never calls it.

## Edge cases & errors

- **E1.** `options: null` means `ParkOptions.Default`. Invalid option values
  throw `ArgumentOutOfRangeException` from the `init` accessor (see the
  Public API section). Validation is inclusive at the stated bounds:
  `EdgeOverlap = 0`, `Reach = 0`, `VisibleFractionThreshold` of 0 or 1, and
  `SmallDivisor = 1` are all valid.
- **E2.** A zero or negative work-area extent is not validated. For example,
  `FitWithin((800, 600), 0, 875)` gives `s = 0`, so the result is `(0, 0)`.
  Negative limits give a negative `s`, so the result is the rounded negative
  scaled size. The functions never throw for these inputs.
- **E3.** A window size of `0 × 0`, or any size with both dimensions `<= 1`,
  is returned unchanged by `FitWithin` (B1).
- **E4.** A minimum with NaN components: `Math.Max(f, NaN)` is NaN, so the
  dimension becomes NaN. No validation is done (garbage in, garbage out).
- **E5.** A degenerate `DisplayFrame` (zero width or height) still produces
  five candidates by the same formulas.
- **E6.** `Candidates` always returns exactly 5 elements, in a new read-only
  list.
- **E7.** `ChooseCandidate` accepts any non-empty list. The Engine always
  passes the 5 candidates.
- **E8.** NaN in `actualVisibleFraction` fails B20, because NaN `<` anything
  is false. B21 then decides the result.
- **E9.** `IsParkFrame` on a display with `DisplayFrame == null` compares
  against the fallback-origin frame (B13 step 2).

## Acceptance criteria

All criteria are [mac] xUnit tests in `tests/CanvasWm.Core.Tests`. They use
no OS access and no TrellisWM. Use exact equality for results the rules make
exact. Use a tolerance of `1e-12` (absolute) for computed fractions. Unless
stated otherwise: default options, `D = (0, 0, 1440, 900)`,
`V = (0, 25, 1440, 875)`, fallback origin `(0, 0)`.

- **AC1 [mac] Single display, fits (Ex. 1).** others `[]`, size `1000×700`,
  no minimum. `ParkSize` = `(1000, 700)`.
  `ParkFrame` = `(1439, 899, 1000, 700)`.
- **AC2 [mac] Too big, and the minimum floor (Ex. 2).** Size `2000×1000`:
  - with no minimum, `ParkSize` = `(1440, 720)` and the frame is
    `(1439, 899, 1440, 720)`;
  - with minimum `1500×800`, the frame is `(1439, 899, 1500, 800)`;
  - with minimum `1499.2×799.5`, the frame is `(1439, 899, 1500, 800)`.
- **AC3 [mac] Park small (Ex. 3).** Size `1000×700`.
  `ParkSmallSize` = `(417, 292)`.
  `ParkSmallFrame` = `(1439, 899, 417, 292)`.
  It equals `ParkFrame(display, (417, 292), null)`.
- **AC4 [mac] Display to the right (Ex. 4).** others
  `[(1440, 0, 1920, 1080)]`, size `800×600`.
  `Score(c[0].Reach, others)` = `191760`; `Score(c[1].Reach, others)` = `0`;
  `ChooseCandidate` = `1`; `ParkFrame` = `(−799, 899, 800, 600)`.
- **AC5 [mac] Surrounded (Ex. 5).** `D = (0, 0, 1000, 800)`,
  `V = (0, 0, 1000, 800)`, others
  `[(1000,0,1000,800), (−1000,0,1000,800), (0,800,1000,800)]`, size `500×400`.
  Scores of candidates 0–4 are `[30340, 30340, 229540, 229540, 200000]`.
  `ChooseCandidate` = `0`; `ParkFrame` = `(999, 799, 500, 400)`.
- **AC6 [mac] Display below (Ex. 6).** others `[(0, 900, 1440, 900)]`, size
  `800×600`. Scores are `600` for c0 and `600` for c1, and the score for c2
  is `0`. `ParkFrame` = `(1439, 0, 800, 600)`.
- **AC7 [mac] No display frame (Ex. 7).** `DisplayFrame = null`, fallback
  origin `(2559, 1439)`, others `[(5000, 5000, 100, 100)]`, size `800×600`.
  `ParkFrame` = `(2559, 1439, 800, 600)`. The result does not change when
  others is replaced by any other list (no scoring).
- **AC8 [mac] IsParkFrame (Ex. 8).** others `[]`:
  `(1439, 899, 1000, 700)` gives true; `(1439, 899, 2000, 1000)` gives true
  (second test); `(1438, 899, 1000, 700)` gives false;
  `(1439, 899, 1000.5, 700)` gives true; `(1439, 899.5, 1000, 700)` gives
  false.
- **AC9 [mac] RestsParked (Ex. 9).** Screens `[D]`. The fraction is computed
  with `VisibleFraction(actual, screens)` and asserted within `1e-12`.
  - target `(1439, 899, 1000, 700)`:
    - actual `(1439, 899, 990, 700)` gives false;
    - actual `(1300, 600, 1000, 700)` has fraction `0.06` and gives false;
    - actual `(1439.5, 860, 1000, 700)` has fraction `20/700000` and gives
      true.
  - target `(1439, 899, 10, 100)`:
    - actual `(1439, 840, 10, 100)` has fraction `0.06` and gives true;
    - actual `(1439, 830, 10, 100)` has fraction `0.07` and gives false.
- **AC10 [mac] Candidate table.** `Candidates(D, (800, 600))` equals exactly:
  - spots `(1439,899,800,600)`, `(−799,899,800,600)`, `(1439,0,800,600)`,
    `(−799,0,800,600)`, `(0,899,800,600)`;
  - reaches `(1439,840,800,660)`, `(−799,840,800,660)`, `(1439,0,800,660)`,
    `(−799,0,800,660)`, `(0,840,800,660)`.

  The count is 5.
- **AC11 [mac] Custom options.** With `EdgeOverlap = 0, Reach = 0`,
  `Candidates(D, (800, 600))` gives:
  - spots `(1440,900,…)`, `(−800,900,…)`, `(1440,0,…)`, `(−800,0,…)`,
    `(0,900,…)`;
  - reach equal to spot for every candidate.

  With `Reach = 30, Tolerance = 2` and target `(0, 100, 10, 10)`:
  - actual Y `68` with X `1.9` gives true;
  - actual Y `67.9` gives false;
  - actual Y `102` gives true;
  - actual Y `102.1` gives false.

  All of these use fraction `1`.
- **AC12 [mac] Positions not rounded.** `D = (0.5, 0.25, 1000.5, 800.25)`,
  `V = (0, 0, 1000, 800)`, others `[]`, size `100×100`. `ParkFrame` =
  `(1000, 799.5, 100, 100)`.
- **AC13 [mac] Rounding mode.** `FitWithin((4, 5), 2, 100)` = `(2, 3)`. The
  half rounds away from zero; banker's rounding would give 2.
- **AC14 [mac] Never enlarge, no rounding when unscaled.**
  - `FitWithin((100.3, 50.7), 1440, 875)` = `(100.3, 50.7)`;
  - `FitWithin((1440, 875), 1440, 875)` = `(1440, 875)` (`s = 1`);
  - `ParkSize((300, 200), V, null)` = `(300, 200)`.
- **AC15 [mac] Degenerate axis.**
  - `FitWithin((1, 2000), 1440, 875)` = `(0, 875)`: the width axis is
    skipped, `s = 0.4375` and `Round(0.4375) = 0`;
  - `FitWithin((0.5, 0.5), 10, 10)` = `(0.5, 0.5)`;
  - `FitWithin((0, 2000), 1440, 875)` = `(0, 875)`;
  - `FitWithin((2000, 1), 1440, 875)` = `(1440, 1)`, from `Round(0.72)`.
- **AC16 [mac] Minimum after fit.**
  - `ParkSize((2000, 1000), V, (100, 900.1))` = `(1440, 901)`;
  - `ParkSize((500, 400), V, (600, 0))` = `(600, 400)`;
  - `ParkSize((500, 400), V, null)` = `(500, 400)`.
- **AC17 [mac] Park-small variants.**
  - `ParkSmallSize((300, 200), V)` = `(300, 200)`, which is not enlarged;
  - with `SmallDivisor = 2`, `ParkSmallSize((1000, 700), V)` = `(625, 438)`.
    Here `s = 437.5/700 = 0.625`, `625` stays `625` and `437.5` rounds to
    `438`;
  - `ParkSmallFrame` never applies a minimum, even where `ParkFrame` with a
    large minimum would differ.
- **AC18 [mac] Scoring helpers.**
  - `IntersectionArea((0,0,10,10), (10,0,10,10))` = `0`, because the rects
    only touch;
  - `IntersectionArea((0,0,10,10), (5,5,10,10))` = `25`;
  - `IntersectionArea` with a zero-width rect = `0`;
  - `Score` over `[]` = `0`;
  - `ChooseCandidate` with others `[]` = `0`;
  - `ChooseCandidate` where every score is non-zero returns the lowest score
    and the earliest index on a tie: build candidates with reach scores
    `[5, 3, 3, 4, 9]` against one other rect and expect `1`;
  - an empty candidate list throws `ArgumentException`.
- **AC19 [mac] RestsParked boundaries** (target `(0, 100, 10, 10)`, defaults):
  - width difference exactly `1` gives false;
  - width difference `0.999` with matching position gives true;
  - fraction exactly `0.05` with position off (X `+5`) gives false;
  - fraction `0.0499` with position off gives true;
  - Y `39` gives true;
  - Y `38.999` gives false;
  - Y `101` gives true;
  - Y `101.001` gives false;
  - X difference exactly `1` gives false;
  - a NaN width in actual gives false;
  - a NaN fraction with matching position gives true;
  - `RestsParked(t, t, f)` is true for `f` in `{0, 0.05, 0.5, 1}`.
- **AC20 [mac] IsParkFrame against changed displays and NaN.** The frame
  `(1439, 899, 800, 600)` is a park frame when others is `[]`. It is **not** a
  park frame when others is `[(1440, 0, 1920, 1080)]`. A frame with any NaN
  component gives false. With `DisplayFrame = null` and origin `(2559, 1439)`,
  `(2559, 1439, 800, 600)` gives true and `(2559, 1440, 800, 600)` gives
  false.
- **AC21 [mac] VisibleFraction.**
  - a rect fully inside one screen gives `1`;
  - a rect half on one screen gives `0.5`;
  - screens `[]` gives `0`;
  - a rect with zero width gives `0`, and so does one with negative height;
  - a rect inside two identical overlapping screens gives `2`, because
    overlaps are not de-duplicated;
  - a rect split across two adjacent screens gives `1`.
- **AC22 [mac] DefaultFallbackOrigin.**
  - `[(0,0,1440,900), (1440,0,1920,1080)]` gives `(3359, 1079)`;
  - `[(0,0,1000,800), (−1000,0,2000,600)]` gives `(999, 799)`. Both screens
    have `MaxX = 1000`, so the first one wins;
  - `[]` gives `(10000, 10000)`;
  - with `EdgeOverlap = 0`, the first list gives `(3360, 1080)`.
- **AC23 [mac] Options.**
  - `ParkOptions.Default` has `(1, 60, 1, 0.05, 3)`;
  - `new ParkOptions() == ParkOptions.Default` (value equality);
  - each of these throws `ArgumentOutOfRangeException`: EdgeOverlap `−0.1`,
    `NaN` or `+∞`; Reach `−1` or `NaN`; Tolerance `0`, `−1` or `NaN`;
    VisibleFractionThreshold `−0.01`, `1.01` or `NaN`; SmallDivisor `0.5`,
    `NaN` or `+∞`;
  - EdgeOverlap `0`, Reach `0`, threshold `0` and `1`, and SmallDivisor `1`
    are accepted.
- **AC24 [mac] Null arguments and snapshot.**
  - each of these throws `ArgumentNullException`: `new ParkDisplay(…, null, …)`,
    `ParkFrame(null, …)`, `ParkSmallFrame(null, …)`, `IsParkFrame(null, …)`,
    `Score(r, null)`, `ChooseCandidate(null, …)`, `ChooseCandidate(c, null)`,
    `VisibleFraction(r, null)`, `DefaultFallbackOrigin(null)`;
  - after a `List<Rect>` is passed to `ParkDisplay` and the list is then
    cleared, `OtherDisplays.Count` and `ParkFrame` results are unchanged.
- **AC25 [mac] Seeded property test.** Use `new Random(seed)` for seeds
  1–200, with 50 cases per seed. Generate:
  - a display frame `D` with integer X, Y in `[−4000, 4000]` and integer
    Width, Height in `[400, 3000]`;
  - `V` with the same size as `D` minus an integer in `[0, 80]` on the
    height;
  - 0–4 other displays with integer coordinates in `[−8000, 8000]` and sizes
    in `[200, 3000]` (they may overlap anything);
  - an integer window size in `[50, 4000]²`;
  - an optional integer minimum in `[0, 3500]²` (present 50% of the time);
  - `DisplayFrame = null` in 20% of cases, with an integer fallback origin;
  - `ParkOptions` either the default, or with integer EdgeOverlap in
    `[0, 3]`, integer Reach in `[0, 100]` and the other values at their
    defaults.

  Then check the following. Coordinates are compared exactly; areas are
  compared within `1e-9`.
  - **P1 (own-display overlap).** Applies when `DisplayFrame` is present,
    `o >= 1`, and the park size `(W, H)` satisfies `o <= W <= D.Width` and
    `o <= H <= D.Height`. Let `k` be the chosen index and
    `I = Spot ∩ D` (as a rect). Then `I.Width × I.Height` equals `o × o` for
    `k ∈ {0, 1}`, `o × H` for `k ∈ {2, 3}` and `W × o` for `k = 4`. For every
    `k`, `Spot` is not contained in `D`.
  - **P2 (recognised).**
    `IsParkFrame(display, ParkFrame(display, size, minimum, opts), opts)` is
    true. `IsParkFrame(display, ParkSmallFrame(display, size, opts), opts)`
    is true.
  - **P3 (choice rule).** The result equals `c[k].Spot` for the `Candidates`
    list `c`, where `k` is the first index with score 0. If there is none,
    `k` is the first index of the minimum score.
  - **P4 (determinism).** Calling `ParkFrame`, `IsParkFrame`, `Candidates`
    and `VisibleFraction` twice with the same inputs, including with a freshly
    built equal `ParkDisplay`, gives identical results (`==` per component).
  - **P5 (size bounds).** With no minimum, `0 <= W <= size.Width`,
    `0 <= H <= size.Height`, and either `(W, H) == size` or
    (`W <= V.Width + 0.5` and `H <= V.Height + 0.5`).
  - **P6 (at rest at target).** `RestsParked(f, f, x, opts)` is true for the
    computed frame `f` and `x` drawn from `[0, 1]`.
- **AC26 [mac] Build hygiene.**
  - `dotnet build CanvasWm.slnx` succeeds with warnings as errors;
  - `CanvasWm.Core` gains no new project or package references;
  - the existing `Rect` and `Size` declarations are byte-for-byte unchanged.

[windows] criteria: none. Behaviour on a real desktop (the sliver against DWM
invisible borders, mixed DPI, `WM_DISPLAYCHANGE`) is verified in #14
win32-parking.

## Assumptions

- **A1. New `Point` type in `CanvasWm.Core.Geometry`.** The fallback origin
  needs a point and no geometry spec defines one. A positional
  `readonly record struct Point(double X, double Y)` is added there. This adds
  a type and changes none (layout-tree A9 allows later features to add
  geometry). Other features should reuse it.
- **A2. `Rect` / `Size` unchanged.** `MaxX`, `MaxY` and intersection are
  computed inside `ParkGeometry` (`IntersectionArea` is public), not added as
  members of `Rect`. A later geometry feature may move them into `Rect`
  without changing behaviour.
- **A3. What `otherDisplays` holds is caller-supplied.** *Needs human
  review.* The study does not show whether the "other displays" are full
  frames or work areas, or whether they include mirrored or inactive
  displays. Core treats them as opaque rects. The intended default for the
  Windows Engine/adapter is the other monitors' **full frames**
  (`rcMonitor`), because the name suggests it. Using work areas would let a
  park spot cover another monitor's taskbar without scoring.
- **A4. `ParkDisplay` copies its list.** This makes the input immutable
  (CLAUDE.md prefers immutable records). As a result, record equality on
  `OtherDisplays` compares references (noted in the Public API section).
- **A5. `DefaultFallbackOrigin` is included as a pure helper.** *Needs human
  review.* The approved scope says the fallback origin is an input, and it
  still is: `ParkFrame` never computes it. The observed macOS rule (bottom-right
  of the right-most screen, inset by the sliver, or `(10000, 10000)` when
  there are no screens) is still provided in Core. That keeps the decision
  out of Win32 (CLAUDE.md: Win32 makes no policy decisions). It can be
  dropped without affecting anything else. On Windows, `(10000, 10000)` with
  no monitors is arbitrary.
- **A6. Visible fraction counts overlapping screens more than once.** This
  matches the observed behaviour. Windows monitors do not overlap in normal
  configurations. The result can exceed 1 only for overlapping or duplicated
  inputs, such as mirrored monitors reported twice. The adapter should pass
  distinct monitor rects.
- **A7. NaN-safe comparisons.** The size gate in B19 is written as "both
  differences `< t`", not "either difference `>= t`". The two are the same
  for finite values; with NaN, B19 rejects. Likewise `IsParkFrame` uses
  `==`, so NaN is never a park frame. These choices only affect invalid
  input.
- **A8. Degenerate-axis guard simplified to "skip the axis".** The observed
  code compares against the raw visible extent instead. That differs only
  when a work-area extent is below 1 DIP, which cannot happen on a real
  display.
- **A9. Option validation ranges** (E1) are CanvasWM choices. A negative
  overlap or reach has no meaning. A zero tolerance would make `RestsParked`
  reject even an exact size match, because of the strict `<`. A divisor
  below 1 would make park-small larger than the plain park size.
- **A10. The defaults are the macOS values, and the units are DIPs.**
  *Needs human review in #14.* On Windows a window may stay fully off screen
  and is not pushed back. The invisible DWM resize border also means a 1-DIP
  overlap of `GetWindowRect` may show nothing. #14 decides the Windows
  values (for example `EdgeOverlap = 0`, or measuring on extended frame
  bounds). This spec only makes them parameters.
- **A11. The park-small divisor applies per axis to the work area**, not as a
  scale factor on the window. This was observed: one third of the work-area
  width and one third of its height.

## Traceability

| Spec item | Findings rule / example |
|---|---|
| ParkDisplay, A3, A4 | rules 1–4; Unknowns (`otherDisplays` content) |
| B1–B4, AC13–AC15, A8 | rules 5, 6, 8 |
| B5, AC2, AC16 | rule 7; Ex. 2 |
| B6, B7, AC3, AC17, A11 | rule 9; Ex. 3 |
| B8–B10, AC10, AC11, AC12, AC25 P1 | rules 10, 11; Ex. 4–6 |
| B11, B12, AC4–AC6, AC18, AC25 P3 | rule 12; Ex. 4–6 |
| B13, AC1, AC7, AC12 | rules 8, 12, 13; Ex. 1, 7 |
| B14, B15, AC24, AC25 P4 | rule 15 (purity, no remembered state) |
| B16–B18, AC8, AC20, AC25 P2 | rules 14, 15; Ex. 8 |
| B19–B22, AC9, AC19, AC25 P6, A7 | rules 16–18, 20 (threshold strictness); Ex. 9 |
| B23, B24, AC21, A6 | rule 19 |
| B25, AC22, A5, `Point` (A1) | rule 13 (adapter origin, 10000 fallback) |
| ParkOptions, AC11, AC23, A9, A10 | Data model constants; Windows notes |
| Out of scope (Engine/#14) | rules 20–26; Unknowns (`parkSettle`, refused park, `parkedSmall`) |
