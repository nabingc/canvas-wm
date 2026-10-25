using System.Runtime.InteropServices;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Windows;

/// <summary>Display enumeration (B19). The caller establishes the DPI scope.</summary>
internal static unsafe class Monitors
{
    /// <summary>Fallback DPI when a display's DPI cannot be read (A16).</summary>
    private const uint FallbackDpi = 96;

    /// <summary>Enumerates displays in enumeration order.</summary>
    internal static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var handles = new List<nint>();
        GCHandle gc = GCHandle.Alloc(handles);
        try
        {
            NativeMethods.EnumDisplayMonitors(0, null, &EnumProc, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        var result = new List<MonitorInfo>(handles.Count);
        foreach (nint hm in handles)
        {
            var mi = new NativeMonitorInfo { cbSize = (uint)sizeof(NativeMonitorInfo) };
            if (!NativeMethods.GetMonitorInfoW(hm, ref mi))
            {
                continue;
            }

            PixelRect bounds = mi.rcMonitor.ToPixelRect();
            uint dpi = NativeMethods.GetDpiForMonitor(hm, NativeConstants.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX != 0
                ? dpiX
                : FallbackDpi;
            result.Add(new MonitorInfo(hm, bounds, dpi, DipSpace.ToDip(bounds, bounds, dpi)));
        }

        return result;
    }

    [UnmanagedCallersOnly]
    private static int EnumProc(nint hMonitor, nint hdc, NativeRect* rect, nint lParam)
    {
        try
        {
            if (GCHandle.FromIntPtr(lParam).Target is List<nint> list)
            {
                list.Add(hMonitor);
            }
        }
        catch (Exception)
        {
            // Must not cross the native boundary.
        }

        return 1;
    }
}
