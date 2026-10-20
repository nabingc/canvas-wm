using System.Runtime.InteropServices;

namespace CanvasWm.Win32.Interop;

/// <summary>kernel32 entry points.</summary>
internal static unsafe partial class NativeMethods
{
    private const string Kernel32 = "kernel32.dll";

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport(Kernel32, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    [LibraryImport(Kernel32)]
    internal static partial int GetApplicationUserModelId(nint hProcess, ref uint applicationUserModelIdLength, char* applicationUserModelId);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(nint hProcess, out ulong lpCreationTime, out ulong lpExitTime, out ulong lpKernelTime, out ulong lpUserTime);

    [LibraryImport(Kernel32)]
    internal static partial nint GetCurrentProcess();
}
