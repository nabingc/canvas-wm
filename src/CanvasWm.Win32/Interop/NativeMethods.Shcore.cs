using System.Runtime.InteropServices;

namespace CanvasWm.Win32.Interop;

/// <summary>shcore entry points.</summary>
internal static unsafe partial class NativeMethods
{
    private const string Shcore = "shcore.dll";

    [LibraryImport(Shcore)]
    internal static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
