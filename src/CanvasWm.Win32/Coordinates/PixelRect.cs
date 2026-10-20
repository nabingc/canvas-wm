namespace CanvasWm.Win32.Coordinates;

/// <summary>A rectangle in physical (device) pixels of the Windows virtual screen, as Win32
/// RECT: Right and Bottom are exclusive. No validation; Width/Height may be negative.</summary>
/// <param name="Left">Left edge.</param>
/// <param name="Top">Top edge.</param>
/// <param name="Right">Right edge (exclusive).</param>
/// <param name="Bottom">Bottom edge (exclusive).</param>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Right - Left.</summary>
    public int Width => Right - Left;

    /// <summary>Bottom - Top.</summary>
    public int Height => Bottom - Top;
}
