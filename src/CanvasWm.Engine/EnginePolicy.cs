namespace CanvasWm.Engine;

/// <summary>User-facing behaviour settings of one engine. Immutable; change with <c>with</c> and assign
/// to <see cref="DisplayEngine.Policy"/>. Values are not validated here; consumers clamp what they read.</summary>
public sealed record EnginePolicy
{
    /// <summary>A policy with every default.</summary>
    public static EnginePolicy Default { get; } = new();


    // --- layout
    /// <summary>Space between tiles, DIPs. Default 10. Relayout trigger.</summary>
    public double Gap { get; init; } = 10;
    /// <summary>Use a fixed minimum tile size instead of learned minimums. Default false. Relayout trigger.</summary>
    public bool FixedMinimum { get; init; } = false;
    /// <summary>Allow tiles below their minimum when space runs out. Default false.</summary>
    public bool SqueezeBelowMinimum { get; init; } = false;
    /// <summary>Share a removed tile's space with all siblings (true) or give it to the neighbour (false). Default true.</summary>
    public bool ShareRemovedSpace { get; init; } = true;
    /// <summary>Maximum number of tiled windows; null = no cap. Default null.</summary>
    public int? MaxDockedTiles { get; init; } = null;
    /// <summary>Collapse very small groups. Default true.</summary>
    public bool CollapseSmallGroups { get; init; } = true;
    /// <summary>A single floating window takes the desktop. Default true.</summary>
    public bool SoleFloatTakesDesktop { get; init; } = true;
    /// <summary>Park out-of-view windows at the small park size. Default false.</summary>
    public bool ParkOutOfViewSmall { get; init; } = false;

    // --- tabs
    /// <summary>Tab groups enabled. Default true.</summary>
    public bool TabsAllowed { get; init; } = true;
    /// <summary>Tab strip style. Default Standard.</summary>
    public TabStyle TabStyle { get; init; } = TabStyle.Standard;
    /// <summary>Tab tint strength. Default 0.</summary>
    public double TabTint { get; init; } = 0;
    /// <summary>Tab padding, part of the tab-strip height, DIPs. Default 4. Relayout trigger.</summary>
    public double TabPadding { get; init; } = 4;
    /// <summary>Show close buttons on tabs. Default true.</summary>
    public bool TabCloseButtons { get; init; } = true;
    // SPEC-ASSUMPTION A6: default true (human decision), Windows convention.
    /// <summary>Close button on the right of a tab. Default true.</summary>
    public bool TabCloseOnRight { get; init; } = true;
    /// <summary>Tab inset, DIPs. Default 0.</summary>
    public double TabInset { get; init; } = 0;
    /// <summary>Tuck tab strips. Default false.</summary>
    public bool TuckStrips { get; init; } = false;

    // --- motion
    /// <summary>Gestures commit real window frames. Default false.</summary>
    public bool GesturesLand { get; init; } = false;
    /// <summary>Touchpad two-finger pinch/pan drives the camera. Default true.</summary>
    public bool TouchPinchPan { get; init; } = true;
    /// <summary>A new motion may interrupt a landing. Default false.</summary>
    public bool InterruptLandings { get; init; } = false;
    /// <summary>Keep pushed windows where they were pushed. Default false.</summary>
    public bool KeepPushed { get; init; } = false;

    // --- drag
    /// <summary>Drags show previews. Default false.</summary>
    public bool PreviewDrags { get; init; } = false;
    /// <summary>Gap handles are shown at rest. Default true.</summary>
    public bool DeskGapHandles { get; init; } = true;
    /// <summary>Hover time before a drop target engages, seconds. Default 0.15.</summary>
    public double DropHold { get; init; } = 0.15;
    /// <summary>Reflow other tiles live while dragging. Default false.</summary>
    public bool ReflowWhileDragging { get; init; } = false;

    // --- views
    /// <summary>Saved views (framings 1-9) enabled. Default true.</summary>
    public bool SavedViewsAllowed { get; init; } = true;
    /// <summary>Overview presentation variant. Default false.</summary>
    public bool OverviewSurfaced { get; init; } = false;
    /// <summary>Desktop edge fraction. Default 0.2.</summary>
    public double DesktopEdge { get; init; } = 0.2;

    // --- overlay
    /// <summary>Picture look. Default Standard.</summary>
    public PreviewStyle PreviewStyle { get; init; } = PreviewStyle.Standard;
    /// <summary>Tint opacity. Default 0.72.</summary>
    public double TintOpacity { get; init; } = 0.72;
    /// <summary>Cover fade amount. Default 0.14.</summary>
    public double CoverFade { get; init; } = 0.14;
    /// <summary>Show the app icon on window covers. Default true.</summary>
    public bool CoverIcon { get; init; } = true;
    // SPEC-ASSUMPTION A6: default 8 (human decision), matches Windows 11.
    /// <summary>Corner radius of drawn windows, DIPs. Default 8.</summary>
    public double WindowCornerRadius { get; init; } = 8;
    /// <summary>Stand-in picture scale. Default Medium.</summary>
    public ProxyScale ProxyScale { get; init; } = ProxyScale.Medium;
    /// <summary>What covers a window while it is resized. Default Picture.</summary>
    public ResizeCover ResizeCover { get; init; } = ResizeCover.Picture;
    /// <summary>Background blur radius, DIPs. Default 12. Advisory on Windows.</summary>
    public double BlurRadius { get; init; } = 12;
    /// <summary>Dim tiled windows that are not focused. Default true.</summary>
    public bool DimDocked { get; init; } = true;
    /// <summary>Dim opacity. Default 0.25.</summary>
    public double DimOpacity { get; init; } = 0.25;
    /// <summary>Dim blur radius. Default 16. Advisory on Windows.</summary>
    public double DimBlur { get; init; } = 16;

    /// <summary>True when the two policies differ in Gap, FixedMinimum or TabPadding.</summary>
    /// <param name="before">The old policy.</param>
    /// <param name="after">The new policy.</param>
    /// <returns>Whether a relayout is needed.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool RequiresRelayout(EnginePolicy before, EnginePolicy after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return before.Gap != after.Gap || before.FixedMinimum != after.FixedMinimum
            || before.TabPadding != after.TabPadding;
    }
}

// SPEC-ASSUMPTION A5: enum case names are CanvasWM's own; only the defaults' raw values are observed.
/// <summary>Picture look.</summary>
public enum PreviewStyle { /// <summary>Plain.</summary>
    Plain = 0, /// <summary>Standard.</summary>
    Standard = 1, /// <summary>Detailed.</summary>
    Detailed = 2 }

/// <summary>Tab strip style.</summary>
public enum TabStyle { /// <summary>Standard.</summary>
    Standard = 0, /// <summary>Compact.</summary>
    Compact = 1 }

/// <summary>Stand-in picture scale.</summary>
public enum ProxyScale { /// <summary>Small.</summary>
    Small = 0, /// <summary>Medium.</summary>
    Medium = 1, /// <summary>Large.</summary>
    Large = 2 }

/// <summary>What covers a window while it is resized.</summary>
public enum ResizeCover { /// <summary>Nothing.</summary>
    None = 0, /// <summary>A picture.</summary>
    Picture = 1, /// <summary>A blur.</summary>
    Blur = 2 }
