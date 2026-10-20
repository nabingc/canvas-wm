using System.Runtime.InteropServices;

namespace CanvasWm.Win32.Interop;

/// <summary>dwmapi entry points.</summary>
internal static unsafe partial class NativeMethods
{
    private const string Dwmapi = "dwmapi.dll";

    [LibraryImport(Dwmapi, EntryPoint = "DwmGetWindowAttribute")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out uint pvAttribute, uint cbAttribute);

    [LibraryImport(Dwmapi, EntryPoint = "DwmGetWindowAttribute")]
    internal static partial int DwmGetWindowAttributeRect(nint hwnd, uint dwAttribute, out NativeRect pvAttribute, uint cbAttribute);
}
