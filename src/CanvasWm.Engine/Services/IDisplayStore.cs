namespace CanvasWm.Engine.Services;

/// <summary>Saved arrangements keyed by engine key.</summary>
public interface IDisplayStore
{
    /// <summary>The saved record, or null when none exists or it cannot be read.</summary>
    /// <param name="key">The engine key.</param>
    /// <returns>The record or null.</returns>
    SavedDisplay? Load(EngineKey key);

    /// <summary>Replace the record for key. Never throws for storage failures.</summary>
    /// <param name="key">The engine key.</param>
    /// <param name="display">The record.</param>
    void Save(EngineKey key, SavedDisplay display);
}
