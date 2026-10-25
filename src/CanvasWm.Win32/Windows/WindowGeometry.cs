using CanvasWm.Core.Geometry;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Windows;

/// <summary>The one code path that reads a window's visible frame in DIPs (facts and frame
/// read-backs share it, #10 B10 / #11 B14). The caller establishes the DPI scope.</summary>
internal static unsafe class WindowGeometry
{
    /// <summary>Reads the frame (extended frame bounds, else the window rect) and maps it to DIPs
    /// through the window's nearest monitor and that monitor's effective DPI.</summary>
    /// <param name="h">The window.</param>
    /// <returns>The frame, or null when it cannot be read.</returns>
    internal static Rect? ReadDipFrame(nint h)
    {
        Read(h, out _, out _, out Rect? frame);
        return frame;
    }

    /// <summary>Full read. Returns false (frame null) when the nearest monitor cannot be
    /// determined; windowRect is then still set when it could be read.</summary>
    internal static bool Read(nint h, out PixelRect? windowRect, out PixelRect monitor, out Rect? frame)
    {
        frame = null;
        monitor = default;

        bool haveWindowRect = NativeMethods.GetWindowRect(h, out NativeRect wr);
        windowRect = haveWindowRect ? wr.ToPixelRect() : null;

        PixelRect? physical = null;
        if (NativeMethods.DwmGetWindowAttributeRect(h, NativeConstants.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeRect dwm, (uint)sizeof(NativeRect)) == 0)
        {
            physical = dwm.ToPixelRect();
        }
        else if (haveWindowRect)
        {
            physical = wr.ToPixelRect();
        }

        nint hm = NativeMethods.MonitorFromWindow(h, NativeConstants.MONITOR_DEFAULTTONEAREST);
        if (hm == 0)
        {
            return false;
        }

        var mi = new NativeMonitorInfo { cbSize = (uint)sizeof(NativeMonitorInfo) };
        if (!NativeMethods.GetMonitorInfoW(hm, ref mi))
        {
            return false;
        }

        monitor = mi.rcMonitor.ToPixelRect();

        // SPEC-ASSUMPTION A8 (#10): anchor-preserving per-monitor DIP mapping using the monitor's effective DPI.
        if (physical is { } p
            && NativeMethods.GetDpiForMonitor(hm, NativeConstants.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            && dpiX != 0)
        {
            frame = DipSpace.ToDip(p, monitor, dpiX);
        }

        return true;
    }
}
