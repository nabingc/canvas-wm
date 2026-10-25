using CanvasWm.Core.Geometry;
using CanvasWm.Win32.Coordinates;

namespace CanvasWm.Win32.Windows;

/// <summary>One monitor as seen by one operation.</summary>
internal readonly record struct MonitorInfo(nint Handle, PixelRect Bounds, uint Dpi, Rect DipFrame);

/// <summary>Every OS call the window system makes, so the algorithms are testable with a
/// fake. NativeWindowOps is the production implementation (B21).</summary>
internal interface INativeWindowOps
{
    // monitors and z-order (any thread)
    IReadOnlyList<MonitorInfo> Monitors();
    IReadOnlyList<nint> TopLevelWindows();

    // per window
    bool IsLive(nint h, int processId, int threadId);
    bool IsHung(nint h);
    bool IsMinimized(nint h);
    bool IsMaximized(nint h);
    bool IsVisible(nint h);
    bool IsCloaked(nint h);
    int MonitorIndexOf(nint h, IReadOnlyList<MonitorInfo> monitors);
    PixelRect? WindowRect(nint h);
    PixelRect? VisibleRect(nint h);
    Rect? ReadDipFrame(nint h);
    void RestoreNoActivate(nint h);
    void Restore(nint h);
    bool TryDeferBatch(IReadOnlyList<(nint Hwnd, PixelRect Outer)> entries);
    bool SetWindowPos(nint h, PixelRect outer, bool positionOnly);
    bool SetForeground(nint h);

    // input
    (int X, int Y)? CursorPos();
    nint MonitorFromPoint(int x, int y);
    bool ButtonsSwapped();
    bool AsyncKeyDown(int virtualKey);
}
