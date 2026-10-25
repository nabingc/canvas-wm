using CanvasWm.Core.Frames;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Windows;

/// <summary>Pure pixel helpers (B8, B12).</summary>
internal static class FrameMath
{
    /// <summary>(v.Left - i.Left, v.Top - i.Top, v.Right + i.Right, v.Bottom + i.Bottom).</summary>
    internal static PixelRect Expand(PixelRect visible, FrameInsets insets) =>
        new(Sat((long)visible.Left - insets.Left),
            Sat((long)visible.Top - insets.Top),
            Sat((long)visible.Right + insets.Right),
            Sat((long)visible.Bottom + insets.Bottom));

    /// <summary>Moves r (size kept) so that Left and Top are PhysicalCoordinates.Clamp'ed.</summary>
    internal static PixelRect ClampOrigin(PixelRect r)
    {
        int left = PhysicalCoordinates.Clamp(r.Left);
        int top = PhysicalCoordinates.Clamp(r.Top);
        long dx = (long)left - r.Left;
        long dy = (long)top - r.Top;
        return new PixelRect(left, top, Sat(r.Right + dx), Sat(r.Bottom + dy));
    }

    /// <summary>VK_RBUTTON when swapped, else VK_LBUTTON.</summary>
    internal static int PrimaryButtonKey(bool swapped) =>
        swapped ? NativeConstants.VK_RBUTTON : NativeConstants.VK_LBUTTON;

    private static int Sat(long v) => (int)Math.Clamp(v, int.MinValue, int.MaxValue);
}
