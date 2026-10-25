namespace CanvasWm.Engine;

/// <summary>Writer constants that are not tuning values (algorithm literals, findings constants list).</summary>
internal static class FrameWriting
{
    /// <summary>Age after which an expected-frame entry is pruned (strict &gt;), seconds.</summary>
    public const double EchoWindowSeconds = 1.5;
    /// <summary>Per-component match tolerance of an echo (≤), DIPs.</summary>
    public const double EchoTolerance = 2.0;
    /// <summary>A park write counts as accepted when actual ≥ target − this on both axes.</summary>
    public const double AcceptedParkSlack = 2.0;
    /// <summary>Own minimum is lowered when a tile's actual is below it by more than this.</summary>
    public const double OwnMinimumSlack = 2.0;
    /// <summary>A larger-refusal axis qualifies only below this fraction of the visible area.</summary>
    public const double RefusalScreenFactor = 0.8;
    /// <summary>Retarget is "unchanged" when every component differs by at most this.</summary>
    public const double RetargetTolerance = 1.0;
    /// <summary>Known frame matches a target (skip) when every component differs by at most this, DIPs.</summary>
    public const double SkipTolerance = 1.0;
    /// <summary>Drift check logs a tile whose frame moved by more than this on any component.</summary>
    public const double DriftTileLogThreshold = 4.0;
    /// <summary>Settle delay = Policy.CoverFade + this, seconds.</summary>
    public const double SettleExtraSeconds = 0.02;
    /// <summary>Delay between a re-land's generation bump and its landing pass, seconds.</summary>
    public const double ReLandDelaySeconds = 0.034;
}
