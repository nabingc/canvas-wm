using CanvasWm.Engine.Rendering;

namespace CanvasWm.Engine.Services;

/// <summary>One display's overlay. Synchronous: the renderer draws or diffs the plan before returning.</summary>
public interface IRenderer
{
    /// <summary>Render the plan. The plan is immutable, so the renderer may keep it.</summary>
    /// <param name="plan">The plan.</param>
    void Render(RenderPlan plan);
}
