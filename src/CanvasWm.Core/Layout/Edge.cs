namespace CanvasWm.Core.Layout;

/// <summary>Side of a target node on which a new node is placed. Raw values are fixed.</summary>
public enum Edge
{
    /// <summary>Before the target, side by side.</summary>
    Left = 0,
    /// <summary>After the target, side by side.</summary>
    Right = 1,
    /// <summary>Before the target, stacked.</summary>
    Top = 2,
    /// <summary>After the target, stacked.</summary>
    Bottom = 3,
}

/// <summary>Helpers for <see cref="Edge"/>.</summary>
public static class EdgeExtensions
{
    /// <summary>Axis of the split created by inserting on this edge: X for Left/Right, Y for Top/Bottom.</summary>
    /// <param name="edge">The edge.</param>
    public static Axis ToAxis(this Edge edge) =>
        edge is Edge.Left or Edge.Right ? Axis.X : Axis.Y;

    /// <summary>True for Left and Top (the new node goes before the target).</summary>
    /// <param name="edge">The edge.</param>
    public static bool IsLeading(this Edge edge) => edge is Edge.Left or Edge.Top;
}
