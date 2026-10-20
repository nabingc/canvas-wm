namespace CanvasWm.Core.Windows;

/// <summary>What to do with an already tracked window given its current facts.</summary>
public enum TrackedFate
{
    /// <summary>Keep tracking.</summary>
    Keep = 0,
    /// <summary>Remove, but the window may come back.</summary>
    RemoveTemporary = 1,
    /// <summary>Remove for good.</summary>
    RemovePermanent = 2,
}
