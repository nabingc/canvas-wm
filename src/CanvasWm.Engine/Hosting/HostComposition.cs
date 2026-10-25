using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Hosting;

/// <summary>Pure composition helpers.</summary>
public static class HostComposition
{
    /// <summary>The router's engine factory (B48).</summary>
    /// <param name="policy">Engine policy.</param>
    /// <param name="windows">The window system.</param>
    /// <param name="capture">The capture service.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="limits">The limit store.</param>
    /// <param name="displays">The display store.</param>
    /// <param name="renderers">Renderer of a display.</param>
    /// <returns>The factory.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static Func<EngineKey, DisplayInfo, DisplayEngine> EngineFactory(
        EnginePolicy policy, IWindowSystem windows, ICapture capture, IClock clock,
        ILimitStore limits, IDisplayStore displays, Func<DisplayInfo, IRenderer> renderers)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(renderers);
        return (key, d) => new DisplayEngine(key, d.WorkArea, policy,
            new EngineServices(windows, capture, clock, renderers(d), limits, displays));
    }
}
