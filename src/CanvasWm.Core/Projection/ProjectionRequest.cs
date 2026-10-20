using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;

namespace CanvasWm.Core.Projection;

/// <summary>Everything one projection pass needs.</summary>
public sealed record ProjectionRequest
{
    private ProjectionPolicy _policy = ProjectionPolicy.Default;
    private Tuning _tuning = Tuning.Default;
    private IReadOnlySet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, Rect> _floatingUnitRects =
        new Dictionary<string, Rect>(StringComparer.Ordinal);

    /// <summary>Creates a request.</summary>
    /// <param name="document">The display's layout document.</param>
    /// <param name="view">Work area, gap, camera and stretch.</param>
    /// <param name="parkDisplay">Display context for park frames.</param>
    /// <param name="windows">Managed windows by key; snapshotted with an ordinal comparer.</param>
    /// <exception cref="ArgumentNullException">any argument, or any dictionary value, is null.</exception>
    public ProjectionRequest(LayoutDocument document, ProjectionView view, ParkDisplay parkDisplay,
                             IReadOnlyDictionary<string, WindowFacts> windows)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(parkDisplay);
        ArgumentNullException.ThrowIfNull(windows);
        var copy = new Dictionary<string, WindowFacts>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, WindowFacts> kv in windows)
        {
            ArgumentNullException.ThrowIfNull(kv.Key);
            copy[kv.Key] = kv.Value ?? throw new ArgumentNullException(nameof(windows), "A value is null.");
        }
        Document = document;
        View = view;
        ParkDisplay = parkDisplay;
        Windows = copy;
    }

    /// <summary>The layout document.</summary>
    public LayoutDocument Document { get; }

    /// <summary>Work area, gap, camera and stretch.</summary>
    public ProjectionView View { get; }

    /// <summary>Display context for park frames.</summary>
    public ParkDisplay ParkDisplay { get; }

    /// <summary>Managed windows by key (ordinal snapshot).</summary>
    public IReadOnlyDictionary<string, WindowFacts> Windows { get; }

    /// <summary>Default ProjectionPolicy.Default. Null throws ArgumentNullException.</summary>
    public ProjectionPolicy Policy
    {
        get => _policy;
        init => _policy = value ?? throw new ArgumentNullException(nameof(Policy));
    }

    /// <summary>Default Tuning.Default. Null throws ArgumentNullException.</summary>
    public Tuning Tuning
    {
        get => _tuning;
        init => _tuning = value ?? throw new ArgumentNullException(nameof(Tuning));
    }

    /// <summary>Park options; null means ParkOptions.Default.</summary>
    public ParkOptions? ParkOptions { get; init; }

    /// <summary>Panel ids of collapsed groups. Default empty. Snapshotted, ordinal.</summary>
    public IReadOnlySet<string> CollapsedPanels
    {
        get => _collapsed;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(CollapsedPanels));
            _collapsed = new HashSet<string>(value, StringComparer.Ordinal);
        }
    }

    /// <summary>True when the desktop slot is usable (engine rule). Default false.</summary>
    public bool DesktopSlotUsable { get; init; }

    /// <summary>Unit rects of floating panels by panel id. Default empty; a floating panel
    /// without a rect here gets no placement. Snapshotted, ordinal.</summary>
    public IReadOnlyDictionary<string, Rect> FloatingUnitRects
    {
        get => _floatingUnitRects;
        // SPEC-ASSUMPTION A9: floats are placed only when the caller supplies their unit rect.
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(FloatingUnitRects));
            _floatingUnitRects = new Dictionary<string, Rect>(
                value.Select(kv => kv), StringComparer.Ordinal);
        }
    }

    /// <summary>Weights-mode entries to look panel rects up in. Null (default) means
    /// LayoutSolver.LayoutRects(Document.Root) is computed.</summary>
    public LayoutEntries? Entries { get; init; }
}
