using System.Runtime.InteropServices;
using CanvasWm.Core.Geometry;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Windows;

/// <summary>Production INativeWindowOps over user32/dwmapi (B21). Callers establish the DPI scope.</summary>
internal sealed unsafe class NativeWindowOps : INativeWindowOps
{
    private const uint BaseFlags = NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_NOOWNERZORDER;

    public IReadOnlyList<MonitorInfo> Monitors() => CanvasWm.Win32.Windows.Monitors.Enumerate();

    public IReadOnlyList<nint> TopLevelWindows()
    {
        var handles = new List<nint>();
        GCHandle gc = GCHandle.Alloc(handles);
        try
        {
            NativeMethods.EnumWindows(&EnumProc, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        return handles;
    }

    public bool IsLive(nint h, int processId, int threadId)
    {
        if (!NativeMethods.IsWindow(h))
        {
            return false;
        }

        uint tid = NativeMethods.GetWindowThreadProcessId(h, out int pid);
        return tid != 0 && unchecked((int)tid) == threadId && pid == processId;
    }

    public bool IsHung(nint h) => NativeMethods.IsHungAppWindow(h);

    public bool IsMinimized(nint h) => NativeMethods.IsIconic(h);

    public bool IsMaximized(nint h) => NativeMethods.IsZoomed(h);

    public bool IsVisible(nint h) => NativeMethods.IsWindowVisible(h);

    public bool IsCloaked(nint h) =>
        NativeMethods.DwmGetWindowAttribute(h, NativeConstants.DWMWA_CLOAKED, out uint cloaked, sizeof(uint)) == 0 && cloaked != 0;

    public int MonitorIndexOf(nint h, IReadOnlyList<MonitorInfo> monitors)
    {
        nint hm = NativeMethods.MonitorFromWindow(h, NativeConstants.MONITOR_DEFAULTTONEAREST);
        if (hm == 0)
        {
            return -1;
        }

        for (int i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Handle == hm)
            {
                return i;
            }
        }

        return -1;
    }

    public PixelRect? WindowRect(nint h) =>
        NativeMethods.GetWindowRect(h, out NativeRect r) ? r.ToPixelRect() : null;

    public PixelRect? VisibleRect(nint h) =>
        NativeMethods.DwmGetWindowAttributeRect(h, NativeConstants.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeRect r, (uint)sizeof(NativeRect)) == 0
            ? r.ToPixelRect()
            : null;

    public Rect? ReadDipFrame(nint h) => WindowGeometry.ReadDipFrame(h);

    public void RestoreNoActivate(nint h) => NativeMethods.ShowWindow(h, NativeConstants.SW_SHOWNOACTIVATE);

    public void Restore(nint h) => NativeMethods.ShowWindow(h, NativeConstants.SW_RESTORE);

    public bool TryDeferBatch(IReadOnlyList<(nint Hwnd, PixelRect Outer)> entries)
    {
        nint hdwp = NativeMethods.BeginDeferWindowPos(entries.Count);
        if (hdwp == 0)
        {
            return false;
        }

        foreach ((nint hwnd, PixelRect o) in entries)
        {
            hdwp = NativeMethods.DeferWindowPos(hdwp, hwnd, 0, o.Left, o.Top, o.Width, o.Height, BaseFlags);
            if (hdwp == 0)
            {
                // The system has destroyed the batch; stop deferring.
                return false;
            }
        }

        return NativeMethods.EndDeferWindowPos(hdwp);
    }

    public bool SetWindowPos(nint h, PixelRect outer, bool positionOnly) =>
        NativeMethods.SetWindowPos(
            h, 0, outer.Left, outer.Top, outer.Width, outer.Height,
            positionOnly ? BaseFlags | NativeConstants.SWP_NOSIZE : BaseFlags);

    public bool SetForeground(nint h) => NativeMethods.SetForegroundWindow(h);

    public (int X, int Y)? CursorPos() =>
        NativeMethods.GetCursorPos(out NativePoint p) ? (p.X, p.Y) : null;

    public nint MonitorFromPoint(int x, int y) =>
        NativeMethods.MonitorFromPoint(new NativePoint { X = x, Y = y }, NativeConstants.MONITOR_DEFAULTTONEAREST);

    public bool ButtonsSwapped() => NativeMethods.GetSystemMetrics(NativeConstants.SM_SWAPBUTTON) != 0;

    public bool AsyncKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [UnmanagedCallersOnly]
    private static int EnumProc(nint hwnd, nint lParam)
    {
        try
        {
            if (GCHandle.FromIntPtr(lParam).Target is List<nint> list)
            {
                list.Add(hwnd);
            }
        }
        catch (Exception)
        {
            // Must not cross the native boundary.
        }

        return 1;
    }
}
