using System.Diagnostics.CodeAnalysis;

namespace CanvasWm.Core.Layout;

/// <summary>Split direction. X = children side by side (a row);
/// Y = children stacked top to bottom (a column).</summary>
public enum Axis
{
    /// <summary>Children side by side.</summary>
    X = 0,
    /// <summary>Children stacked top to bottom.</summary>
    Y = 1,
}

/// <summary>Helpers for <see cref="Axis"/>.</summary>
public static class AxisExtensions
{
    /// <summary>Returns the other axis (X to Y, Y to X).</summary>
    public static Axis Cross(this Axis axis) => axis == Axis.X ? Axis.Y : Axis.X;

    /// <summary>Raw persisted value: "x" for X, "y" for Y.</summary>
    public static string ToRaw(this Axis axis) => axis == Axis.X ? "x" : "y";

    /// <summary>Parses "x"/"y" (ordinal, exact lowercase). Returns false for anything else.</summary>
    public static bool TryParseRaw(string? raw, out Axis axis)
    {
        if (string.Equals(raw, "x", StringComparison.Ordinal))
        {
            axis = Axis.X;
            return true;
        }
        if (string.Equals(raw, "y", StringComparison.Ordinal))
        {
            axis = Axis.Y;
            return true;
        }
        axis = default;
        return false;
    }
}
