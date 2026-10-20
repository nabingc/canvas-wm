using CanvasWm.Core.Geometry;

namespace CanvasWm.Engine.Services;

/// <summary>Learned minimum window sizes per application.</summary>
public interface ILimitStore
{
    /// <summary>Stored minimum for app, or null when app is null or nothing is stored.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The minimum or null.</returns>
    Size? Minimum(AppId? app);

    /// <summary>Raise the stored minimum per <see cref="LimitRules.Learn"/>. No-op when app is null.</summary>
    /// <param name="app">The application.</param>
    /// <param name="minimum">The learned minimum.</param>
    void Learn(AppId? app, Size minimum);

    /// <summary>Lower the stored minimum per <see cref="LimitRules.Observe"/>. No-op when app is null or nothing is stored.</summary>
    /// <param name="app">The application.</param>
    /// <param name="observed">The observed size.</param>
    void Observe(AppId? app, Size observed);
}
