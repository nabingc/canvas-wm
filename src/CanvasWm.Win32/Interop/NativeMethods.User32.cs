using System.Runtime.InteropServices;

namespace CanvasWm.Win32.Interop;

/// <summary>user32 entry points. No entry point that sends a window message is declared.</summary>
internal static unsafe partial class NativeMethods
{
    private const string User32 = "user32.dll";

    [LibraryImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hWnd);

    [LibraryImport(User32)]
    internal static partial nint GetAncestor(nint hWnd, uint gaFlags);

    [LibraryImport(User32)]
    internal static partial nint GetWindow(nint hWnd, uint uCmd);

    [LibraryImport(User32, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport(User32, SetLastError = true)]
    internal static partial int GetClassNameW(nint hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport(User32)]
    internal static partial int InternalGetWindowText(nint hWnd, char* pString, int cchMaxCount);

    [LibraryImport(User32)]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsHungAppWindow(nint hWnd);

    [LibraryImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [LibraryImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTitleBarInfo(nint hWnd, ref NativeTitleBarInfo pti);

    [LibraryImport(User32)]
    internal static partial nint GetShellWindow();

    [LibraryImport(User32, EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindowExW(nint hWndParent, nint hWndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport(User32)]
    internal static partial nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [LibraryImport(User32, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfoW(nint hMonitor, ref NativeMonitorInfo lpmi);

    [LibraryImport(User32, SetLastError = true)]
    internal static partial nint SetThreadDpiAwarenessContext(nint dpiContext);

    [LibraryImport(User32, SetLastError = true)]
    internal static partial nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> pfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [LibraryImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hWinEventHook);
}
