namespace CanvasWm.Win32.Windows;

/// <summary>Conversion between HWND values and the ulong handles used by OsWindowFacts and
/// RawWindowEvent. Bijective; never throws.</summary>
public static class HandleConversion
{
    /// <summary>unchecked((ulong)(long)hwnd).</summary>
    /// <param name="hwnd">The HWND value.</param>
    /// <returns>The handle.</returns>
    public static ulong ToHandle(nint hwnd) => unchecked((ulong)(long)hwnd);

    /// <summary>unchecked((nint)(long)handle).</summary>
    /// <param name="handle">The handle.</param>
    /// <returns>The HWND value.</returns>
    public static nint ToHwnd(ulong handle) => unchecked((nint)(long)handle);
}
