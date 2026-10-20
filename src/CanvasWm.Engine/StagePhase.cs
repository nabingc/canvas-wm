namespace CanvasWm.Engine;

/// <summary>What the stage is doing. Raw values fixed.</summary>
public enum StagePhase
{
    /// <summary>Resting desk.</summary>
    Desk = 0,
    /// <summary>Camera or layout in motion.</summary>
    Moving = 1,
    /// <summary>Overview shown.</summary>
    Overview = 2,
    /// <summary>Windows landing on real frames.</summary>
    Landing = 3,
    /// <summary>Overlay fading out.</summary>
    Fading = 4,
}
