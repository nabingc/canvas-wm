namespace CanvasWm.Engine;

/// <summary>Window-event constants that are algorithm literals, not tuning values.</summary>
internal static class WindowEvents
{
    /// <summary>Reveal suppression after a reveal, seconds.</summary>
    public const double RevealQuietSeconds = 1.0;
    /// <summary>Reveal suppression after a window is removed, seconds.</summary>
    public const double CloseQuietSeconds = 1.0;
    /// <summary>Minimum spacing between two zoom/un-zoom toggles from app resizes (strict &gt;), seconds.</summary>
    public const double ZoomToggleSpacingSeconds = 1.0;
    /// <summary>A reported frame counts as resized when width or height changed by more than this.</summary>
    public const double ResizedThreshold = 0.5;
    /// <summary>"Grew": wider than the tile by more than this...</summary>
    public const double GrowWidthMargin = 100;
    /// <summary>...or taller than the tile by more than this.</summary>
    public const double GrowHeightMargin = 60;
    /// <summary>"Grew" also needs the other axis to be at least tile − this.</summary>
    public const double GrowKeepSlack = 2;
    /// <summary>"Shrank": narrower or shorter than the tile by more than this.</summary>
    public const double ShrinkMargin = 100;
    /// <summary>"Fills": width at least this fraction of the work area...</summary>
    public const double FillsWidthFraction = 0.92;
    /// <summary>...and height at least this fraction.</summary>
    public const double FillsHeightFraction = 0.90;
    /// <summary>A filling frame un-zooms only if it differs from the tile by more than this on some component.</summary>
    public const double UnzoomDrift = 4;
    /// <summary>Insist learning: an axis is "larger" when frame &gt; tile + this.</summary>
    public const double InsistSizeSlack = 2;
    /// <summary>Insist learning: a larger axis qualifies only below this fraction of the work area.</summary>
    public const double InsistScreenFactor = 0.8;
    /// <summary>Desktop slot (H1) usable only if its docked frame is at least this wide...</summary>
    public const double DesktopSlotMinWidth = 480;
    /// <summary>...and this tall.</summary>
    public const double DesktopSlotMinHeight = 320;
}
