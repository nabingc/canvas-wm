using System.Runtime.InteropServices;
using System.Text;
using CanvasWm.Win32.Tests.WindowEnumSpec;

namespace CanvasWm.Win32.Tests.HostSpec;

/// <summary>Test-only P/Invoke declarations for the app-host tests (independent of the product's interop).</summary>
internal static class HostNative
{
    internal const uint WM_NULL = 0x0000, WM_CLOSE = 0x0010, WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016;
    internal const uint WM_SETTINGCHANGE = 0x001A, WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0, WM_POWERBROADCAST = 0x0218;
    internal const uint WM_USER = 0x0400;
    internal const int GWL_EXSTYLE = -20;
    internal const uint GA_PARENT = 1;
    internal const nint WS_EX_TOOLWINDOW = 0x80;
    internal const int MDT_EFFECTIVE_DPI = 0;

    internal delegate bool EnumThreadWndProc(nint hwnd, nint lParam);

    internal delegate bool MonitorEnumProc(nint hmon, nint hdc, nint rect, nint lParam);

    [DllImport("user32.dll")] internal static extern bool EnumThreadWindows(uint tid, EnumThreadWndProc cb, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassNameW(nint hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetDesktopWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] internal static extern nint SendMessageW(nint hwnd, uint msg, nuint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc cb, nint lParam);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();

    /// <summary>Finds the host window of the given thread by class name, or 0.</summary>
    internal static nint FindHostWindowOfThread(uint nativeThreadId)
    {
        nint found = 0;
        EnumThreadWindows(nativeThreadId, (h, _) =>
        {
            var sb = new StringBuilder(64);
            GetClassNameW(h, sb, sb.Capacity);
            if (sb.ToString() == "CanvasWm.HostWindow") { found = h; return false; }
            return true;
        }, 0);
        return found;
    }

    /// <summary>Monitors read independently, in enumeration order: monitor rectangle, effective DPI.</summary>
    internal static List<(Native.RECT Bounds, Native.RECT Work, uint Dpi, bool Primary)> ReadMonitors()
    {
        return Native.WithPmv2(() =>
        {
            var list = new List<(Native.RECT, Native.RECT, uint, bool)>();
            EnumDisplayMonitors(0, 0, (hmon, _, _, _) =>
            {
                var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
                if (Native.GetMonitorInfoW(hmon, ref mi))
                {
                    uint dpi = 96;
                    if (Native.GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI, out uint x, out _) == 0 && x != 0) dpi = x;
                    list.Add((mi.rcMonitor, mi.rcWork, dpi, (mi.dwFlags & 1) != 0));
                }

                return true;
            }, 0);
            return list;
        });
    }
}
