using System.Runtime.InteropServices;

namespace CanvasWm.Win32.Interop;

/// <summary>Entry points of the host window, message pump, display configuration, composition
/// and waitable timer (#33).</summary>
internal static unsafe partial class NativeMethods
{
    [LibraryImport(User32, EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport(User32, EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport(User32, EntryPoint = "ChangeWindowMessageFilterEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint pChangeFilterStruct);

    [LibraryImport(User32, EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindowW(string? lpClassName, string? lpWindowName);

    [LibraryImport(User32, SetLastError = true)]
    internal static partial uint MsgWaitForMultipleObjectsEx(uint nCount, nint* pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    [LibraryImport(User32, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessageW(out NativeMsg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in NativeMsg lpMsg);

    [LibraryImport(User32, EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessageW(in NativeMsg lpMsg);

    [LibraryImport(User32)]
    internal static partial void PostQuitMessage(int nExitCode);

    [LibraryImport(User32, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfoExW(nint hMonitor, ref NativeMonitorInfoEx lpmi);

    [LibraryImport(User32)]
    internal static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [LibraryImport(User32)]
    internal static partial int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, byte* pathArray,
        ref uint numModeInfoArrayElements, byte* modeInfoArray, nint currentTopologyId);

    [LibraryImport(User32)]
    internal static partial int DisplayConfigGetDeviceInfo(byte* requestPacket);

    [LibraryImport(Dwmapi, EntryPoint = "DwmFlush")]
    internal static partial int DwmFlush();

    [LibraryImport(Dwmapi, EntryPoint = "DwmGetCompositionTimingInfo")]
    internal static partial int DwmGetCompositionTimingInfo(nint hwnd, ref NativeDwmTimingInfo pTimingInfo);

    [LibraryImport(Kernel32, EntryPoint = "CreateWaitableTimerExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWaitableTimerExW(nint lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [LibraryImport(Kernel32, EntryPoint = "SetWaitableTimer", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWaitableTimer(nint hTimer, long* lpDueTime, int lPeriod, nint pfnCompletionRoutine,
        nint lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool fResume);

    [LibraryImport(Kernel32, EntryPoint = "WaitForSingleObject")]
    internal static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);
}
