using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Projection;

/// <summary>One placement per panel.</summary>
/// <param name="PanelId">The panel's id.</param>
/// <param name="Key">The panel's selected window key.</param>
/// <param name="Frame">Un-rounded, un-clamped frame.</param>
/// <param name="Floating">True for a floating panel.</param>
/// <param name="OnStage">Whether the frame counts as on the stage.</param>
/// <param name="Fits">Whether the frame meets the window's minimum.</param>
/// <param name="Strip">Tab-strip rect at rest, or null when the panel has no strip.</param>
/// <param name="StripFullHeight">Full strip height for a panel with a strip; 0 otherwise.</param>
public sealed record PanelPlacement(string PanelId, string Key, Rect Frame, bool Floating,
                                    bool OnStage, bool Fits, Rect? Strip, double StripFullHeight);
