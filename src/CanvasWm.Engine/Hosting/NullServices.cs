using CanvasWm.Engine.Rendering;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Hosting;

/// <summary>IRenderer that draws nothing (until the overlay feature exists).</summary>
public sealed class NullRenderer : IRenderer
{
    private NullRenderer()
    {
    }

    /// <summary>The shared instance.</summary>
    public static NullRenderer Instance { get; } = new();

    /// <inheritdoc />
    public void Render(RenderPlan plan) => ArgumentNullException.ThrowIfNull(plan);
}

/// <summary>ICapture that captures nothing (until the overlay feature exists): Latest is always null.</summary>
public sealed class NullCapture : ICapture
{
    private NullCapture()
    {
    }

    /// <summary>The shared instance.</summary>
    public static NullCapture Instance { get; } = new();

    /// <inheritdoc />
    public void Start(WindowKey key) => ArgumentNullException.ThrowIfNull(key);

    /// <inheritdoc />
    public void Stop(WindowKey key) => ArgumentNullException.ThrowIfNull(key);

    /// <inheritdoc />
    public void SetRate(WindowKey key, double fps) => ArgumentNullException.ThrowIfNull(key);

    /// <inheritdoc />
    public void Hold(WindowKey key, bool on) => ArgumentNullException.ThrowIfNull(key);

    /// <inheritdoc />
    public CaptureFrame? Latest(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return null;
    }
}
