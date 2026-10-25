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

/// <summary>Win32 MONITORINFOEXW (MONITORINFO followed by the 32-char device name).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeMonitorInfoEx
{
    internal uint cbSize;
    internal NativeRect rcMonitor;
    internal NativeRect rcWork;
    internal uint dwFlags;
    internal fixed char szDevice[32];
}

/// <summary>Win32 MSG.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMsg
{
    internal nint hwnd;
    internal uint message;
    internal nint wParam;
    internal nint lParam;
    internal uint time;
    internal NativePoint pt;
}

/// <summary>Win32 DWM_TIMING_INFO. Only RefreshNumerator and RefreshDenominator are read; the
/// rest exists so that cbSize matches the OS structure (dwmapi.h declares it with 1-byte packing).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct NativeDwmTimingInfo
{
    internal uint cbSize;
    internal uint RefreshNumerator;
    internal uint RefreshDenominator;
    internal ulong qpcRefreshPeriod;
    internal uint ComposeNumerator;
    internal uint ComposeDenominator;
    internal ulong qpcVBlank;
    internal ulong cRefresh;
    internal uint cDXRefresh;
    internal ulong qpcCompose;
    internal ulong cFrame;
    internal uint cDXPresent;
    internal ulong cRefreshFrame;
    internal ulong cFrameSubmitted;
    internal uint cDXPresentSubmitted;
    internal ulong cFrameConfirmed;
    internal uint cDXPresentConfirmed;
    internal ulong cRefreshConfirmed;
    internal uint cDXRefreshConfirmed;
    internal ulong cFramesLate;
    internal uint cFramesOutstanding;
    internal ulong cFrameDisplayed;
    internal ulong qpcFrameDisplayed;
    internal ulong cRefreshFrameDisplayed;
    internal ulong cFrameComplete;
    internal ulong qpcFrameComplete;
    internal ulong cFramePending;
    internal ulong qpcFramePending;
    internal ulong cFramesDisplayed;
    internal ulong cFramesComplete;
    internal ulong cFramesPending;
    internal ulong cFramesAvailable;
    internal ulong cFramesDropped;
    internal ulong cFramesMissed;
    internal ulong cRefreshNextDisplayed;
    internal ulong cRefreshNextPresented;
    internal ulong cRefreshesDisplayed;
    internal ulong cRefreshesPresented;
    internal ulong cRefreshStarted;
    internal ulong cPixelsReceived;
    internal ulong cPixelsDrawn;
    internal ulong cBuffersEmpty;
}
