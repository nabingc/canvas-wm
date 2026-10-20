using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine;

// SPEC-ASSUMPTION A19: GesturePhase includes Cancelled (see GesturePhase).
public sealed partial class DisplayEngine
{
    /// <summary>Frame the neighbouring tile on that side of the framed node (or of the focused window's tile when nothing is framed).</summary>
    /// <param name="toward">The side to move toward.</param>
    public void Move(Edge toward) => Run(() => MoveCore(toward));

    /// <summary>Rebuild the tiling as a spiral of all dockable windows in front-to-back order and frame nothing.</summary>
    public void ArrangeAll() => Run(ArrangeAllCore);

    /// <summary>Float every window. (#13)</summary>
    public void FloatAll() => EnsureNotInCallback();

    /// <summary>Toggle one window between floating and tiled. (#13)</summary>
    public void ToggleDocked(WindowKey key) => EnsureNotInCallback();

    /// <summary>Reserve a split next to key for the next new window. (#13)</summary>
    public void SplitForNewWindow(WindowKey key) => EnsureNotInCallback();

    /// <summary>Frame one window alone, or undo; null = focused window. (#13)</summary>
    public void ToggleMaximize(WindowKey? key) => EnsureNotInCallback();

    /// <summary>The pointer is dragging a native window at the given point. (#13)</summary>
    public void NativePointerDragged(Point at) => EnsureNotInCallback();

    /// <summary>The mouse button was released after a native window drag. (#13)</summary>
    public void NativeMouseUp(Point at) => EnsureNotInCallback();

    /// <summary>Frame the whole layout. (#15)</summary>
    public void ShowEverything() => EnsureNotInCallback();

    /// <summary>Move the camera one tree level up. (#15)</summary>
    public void StepOut() => EnsureNotInCallback();

    /// <summary>One level down toward a point. (#15)</summary>
    public void StepIn(Point toward) => EnsureNotInCallback();

    /// <summary>One level down into a node. (#15)</summary>
    public void StepInto(string nodeId) => EnsureNotInCallback();

    /// <summary>Camera history back. (#15)</summary>
    public void Back() => EnsureNotInCallback();

    /// <summary>Camera history forward. (#15)</summary>
    public void Forward() => EnsureNotInCallback();

    /// <summary>Zoom in toward a point (null = centre). (#15)</summary>
    public void ZoomIn(Point? toward) => EnsureNotInCallback();

    /// <summary>Zoom out one step. (#15)</summary>
    public void ZoomOut() => EnsureNotInCallback();

    /// <summary>Zoom into a node. (#15)</summary>
    public void ZoomInto(string nodeId) => EnsureNotInCallback();

    /// <summary>Go to saved view 1-9. (#15)</summary>
    public void GoToFraming(int slot) => EnsureNotInCallback();

    /// <summary>Save the current view in slot 1-9. (#15)</summary>
    public void SaveFraming(int slot) => EnsureNotInCallback();

    /// <summary>Forget saved view 1-9. (#15)</summary>
    public void ForgetFraming(int slot) => EnsureNotInCallback();

    /// <summary>Frame the desktop node. (#15)</summary>
    public void ShowDesktop() => EnsureNotInCallback();

    /// <summary>Show or hide the desktop node. (#15)</summary>
    public void SetDesktopHidden(bool hidden) => EnsureNotInCallback();

    /// <summary>Toggle framing the desktop node. (#15)</summary>
    public void ToggleDesktop() => EnsureNotInCallback();

    /// <summary>Toggle desktop node visibility. (#15)</summary>
    public void ToggleDesktopHidden() => EnsureNotInCallback();

    /// <summary>Scroll input (wheel or touchpad). (#15)</summary>
    public void Scroll(double dx, double dy, bool precise, bool zoomModifier, Point at,
                       GesturePhase phase, bool momentumEnded) => EnsureNotInCallback();

    /// <summary>Pinch input; alternate = the alternate modifier is held. (#15)</summary>
    public void Pinch(double by, Point at, GesturePhase phase, bool alternate, bool overWallpaper) => EnsureNotInCallback();

    /// <summary>Touch pinch-and-pan combined step. (#15)</summary>
    public void TouchPinchPan(double zoom, Point around, double dx, double dy) => EnsureNotInCallback();

    /// <summary>A touch gesture ended. (#15)</summary>
    public void TouchGestureEnded() => EnsureNotInCallback();

    /// <summary>Begin a rectangle zoom. (#15)</summary>
    public void BeginRectZoom(Point at) => EnsureNotInCallback();

    /// <summary>Move the rectangle-zoom corner. (#15)</summary>
    public void MoveRectZoom(Point to) => EnsureNotInCallback();

    /// <summary>Finish the rectangle zoom. (#15)</summary>
    public void EndRectZoom(Point at) => EnsureNotInCallback();

    /// <summary>Open or close the overview of everything. (#16)</summary>
    public void ToggleOverview() => EnsureNotInCallback();

    /// <summary>Open the overview from a touch gesture. (#16)</summary>
    public void OpenOverviewByTouch() => EnsureNotInCallback();
}
