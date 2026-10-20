using System.Runtime.InteropServices;

namespace CanvasWm.Win32.Tests.WindowEnumSpec;

/// <summary>Test-only P/Invoke declarations (independent of the product's interop).</summary>
internal static class Native
{
    internal const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_MAXIMIZE = 3, SW_SHOW = 5, SW_MINIMIZE = 6, SW_RESTORE = 9;
    internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_VISIBLE = 0x10000000, WS_CHILD = 0x40000000, WS_POPUP = 0x80000000;
    internal const uint WS_EX_TOOLWINDOW = 0x80;
    internal const uint WM_CLOSE = 0x0010, WM_APP = 0x8000;
    internal const uint PM_REMOVE = 1;
    internal const uint QS_ALLINPUT = 0x04FF, MWMO_INPUTAVAILABLE = 0x0004;
    internal const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    internal static readonly nint PerMonitorV2 = -4;
    internal static readonly nint Unaware = -1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowW(string? cls, string? title);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool PostMessageW(nint hwnd, uint msg, nuint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool SetWindowTextW(nint hwnd, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint CreateWindowExW(uint exStyle, string cls, string? title, uint style, int x, int y, int w, int h,
        nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out RECT r);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetMonitorInfoW(nint hmon, ref MONITORINFO mi);
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint ctx);
    [DllImport("user32.dll")] internal static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(nint a, nint b);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool PeekMessageW(out MSG m, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] internal static extern nint DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")]
    internal static extern uint MsgWaitForMultipleObjectsEx(uint count, nint[]? handles, uint ms, uint mask, uint flags);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessageW(uint tid, uint msg, nuint w, nint l);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint hmon, int type, out uint x, out uint y);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, uint attr, out RECT r, int size);

    internal static List<nint> CaptureEnumWindows()
    {
        var list = new List<nint>();
        EnumWindows((h, _) => { list.Add(h); return true; }, 0);
        return list;
    }

    internal static (int Pid, int Tid) PidTid(nint hwnd)
    {
        uint tid = GetWindowThreadProcessId(hwnd, out uint pid);
        return ((int)pid, (int)tid);
    }

    /// <summary>Runs body under a per-monitor-v2 DPI scope on the calling thread.</summary>
    internal static T WithPmv2<T>(Func<T> body)
    {
        nint old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try { return body(); }
        finally { SetThreadDpiAwarenessContext(old); }
    }
}
