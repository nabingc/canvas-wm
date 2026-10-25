using System.Runtime.InteropServices;
using CanvasWm.Win32.Coordinates;

namespace CanvasWm.Win32.Interop;

/// <summary>Win32 RECT.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;

    internal readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);
}

/// <summary>Win32 MONITORINFO.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMonitorInfo
{
    internal uint cbSize;
    internal NativeRect rcMonitor;
    internal NativeRect rcWork;
    internal uint dwFlags;
}

/// <summary>Win32 TITLEBARINFO. rgstate has CCHILDREN_TITLEBAR + 1 = 6 entries.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeTitleBarInfo
{
    internal uint cbSize;
    internal NativeRect rcTitleBar;
    internal fixed uint rgstate[6];
}

/// <summary>Win32 POINT.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    internal int X;
    internal int Y;
}

/// <summary>Win32 WNDCLASSEXW. The window procedure is an unmanaged function pointer, so no managed delegate exists to collect.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeWndClassEx
{
    internal uint cbSize;
    internal uint style;
    internal delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
    internal int cbClsExtra;
    internal int cbWndExtra;
    internal nint hInstance;
    internal nint hIcon;
    internal nint hCursor;
    internal nint hbrBackground;
    internal char* lpszMenuName;
    internal char* lpszClassName;
    internal nint hIconSm;
}
