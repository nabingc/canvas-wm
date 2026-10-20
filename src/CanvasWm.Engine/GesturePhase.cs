namespace CanvasWm.Engine;

/// <summary>Phase of a continuous pointer gesture as translated by the app.</summary>
public enum GesturePhase
{
    /// <summary>The gesture started.</summary>
    Began = 0,
    /// <summary>The gesture continues.</summary>
    Changed = 1,
    /// <summary>The gesture ended normally.</summary>
    Ended = 2,
    /// <summary>The gesture was cancelled.</summary>
    // SPEC-ASSUMPTION A19: Cancelled added because Windows input can be cancelled.
    Cancelled = 3,
}
