using CanvasWm.Core.Geometry;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Routing;

/// <summary>What Execute did (B27).</summary>
/// <param name="Engine">Key of the engine that received the call.</param>
/// <param name="Command">The executed command.</param>
/// <param name="ZoomToward">ZoomIn argument (null = centre); null for other kinds.</param>
/// <param name="SplitKey">SplitForNewWindow argument; null when no call was made or other kinds.</param>
public sealed record CommandRoute(EngineKey Engine, ShortcutCommand Command, Point? ZoomToward,
                                  WindowKey? SplitKey);
