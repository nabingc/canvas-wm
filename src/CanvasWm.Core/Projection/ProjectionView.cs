using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Projection;

/// <summary>The camera-ready projection inputs for one display. Immutable.</summary>
public sealed record ProjectionView
{
    /// <summary>Stretch tolerance: |F.w - F.h| at or below this is square. 1e-9.</summary>
    public const double SquareTolerance = 1e-9;

    /// <summary>Creates a view.</summary>
    /// <param name="workArea">Work area of the display, DIPs. Not validated.</param>
    /// <param name="gap">Gap between tiles, DIPs. NaN, infinity or negative is stored as 0.</param>
    /// <param name="camera">Camera rect in unit space.</param>
    /// <param name="stretch">Stretch applied to unit values before the camera.</param>
    /// <exception cref="ArgumentOutOfRangeException">camera has a non-finite component, or its
    /// Width or Height is not greater than 0.</exception>
    /// <exception cref="ArgumentNullException">stretch is null.</exception>
    public ProjectionView(Rect workArea, double gap, Rect camera, Stretch stretch)
    {
        ArgumentNullException.ThrowIfNull(stretch);
        if (!Stretch.IsValid(camera))
        {
            throw new ArgumentOutOfRangeException(nameof(camera), camera,
                "Camera must be finite with positive Width and Height.");
        }
        // SPEC-ASSUMPTION A14: NaN, infinite or negative gap is stored as 0 (see ScreenProjection.ClampGap).
        WorkArea = workArea;
        Gap = ScreenProjection.ClampGap(gap);
        Camera = camera;
        Stretch = stretch;
        Stage = ScreenProjection.Stage(workArea, Gap);
    }

    /// <summary>Work area of the display.</summary>
    public Rect WorkArea { get; }

    /// <summary>Clamped gap.</summary>
    public double Gap { get; }

    /// <summary>Camera rect in unit space.</summary>
    public Rect Camera { get; }

    /// <summary>Stretch applied before the camera.</summary>
    public Stretch Stretch { get; }

    /// <summary>WorkArea inset by Gap/2 on each side.</summary>
    public Rect Stage { get; }

    /// <summary>At-rest view: Stretch.ForFraming(framed) and ScreenProjection.CameraForFraming(framed).</summary>
    /// <param name="workArea">Work area.</param>
    /// <param name="gap">Gap.</param>
    /// <param name="framed">Unit rect of the framed node, or null when nothing is framed.</param>
    public static ProjectionView AtRest(Rect workArea, double gap, Rect? framed) =>
        new(workArea, gap, ScreenProjection.CameraForFraming(framed), Stretch.ForFraming(framed));
}
