using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Hosting;

/// <summary>DWM composition helpers for the tick thread.</summary>
public static unsafe class DwmComposition
{
    /// <summary>DwmFlush: blocks until the next composition pass. True on S_OK.</summary>
    /// <returns>True when the call succeeded.</returns>
    public static bool Flush()
    {
        try
        {
            return NativeMethods.DwmFlush() == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Composition refresh rate in Hz from DwmGetCompositionTimingInfo(NULL), or null.</summary>
    /// <returns>The rate, or null when it cannot be read.</returns>
    public static double? RefreshRate()
    {
        try
        {
            var info = new NativeDwmTimingInfo { cbSize = (uint)sizeof(NativeDwmTimingInfo) };
            if (NativeMethods.DwmGetCompositionTimingInfo(0, ref info) != 0 || info.RefreshDenominator == 0)
            {
                return null;
            }

            double hz = (double)info.RefreshNumerator / info.RefreshDenominator;
            return double.IsFinite(hz) && hz > 0 ? hz : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
