using CanvasWm.Engine.Services;

namespace CanvasWm.Engine;

/// <summary>The six outbound services one engine uses. Several engines may share the same
/// instances (window system, capture, clock, stores are per-app; the renderer is per display).</summary>
public sealed record EngineServices
{
    /// <summary>Creates the bundle.</summary>
    /// <param name="windows">The window system.</param>
    /// <param name="capture">The capture service.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="renderer">The renderer of this display.</param>
    /// <param name="limits">The learned-limit store.</param>
    /// <param name="displays">The saved-display store.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public EngineServices(IWindowSystem windows, ICapture capture, IClock clock,
                          IRenderer renderer, ILimitStore limits, IDisplayStore displays)
    {
        Windows = windows ?? throw new ArgumentNullException(nameof(windows));
        Capture = capture ?? throw new ArgumentNullException(nameof(capture));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        Displays = displays ?? throw new ArgumentNullException(nameof(displays));
    }

    /// <summary>The window system.</summary>
    public IWindowSystem Windows { get; }

    /// <summary>The capture service.</summary>
    public ICapture Capture { get; }

    /// <summary>The clock.</summary>
    public IClock Clock { get; }

    /// <summary>The renderer of this display.</summary>
    public IRenderer Renderer { get; }

    /// <summary>The learned-limit store.</summary>
    public ILimitStore Limits { get; }

    /// <summary>The saved-display store.</summary>
    public IDisplayStore Displays { get; }
}
