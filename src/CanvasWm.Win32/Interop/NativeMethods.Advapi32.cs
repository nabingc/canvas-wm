using System.Runtime.InteropServices;

namespace CanvasWm.Win32.Interop;

/// <summary>advapi32 entry points.</summary>
internal static unsafe partial class NativeMethods
{
    private const string Advapi32 = "advapi32.dll";

    [LibraryImport(Advapi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport(Advapi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, byte* tokenInformation, uint tokenInformationLength, out uint returnLength);

    [LibraryImport(Advapi32, SetLastError = true)]
    internal static partial byte* GetSidSubAuthorityCount(nint pSid);

    [LibraryImport(Advapi32, SetLastError = true)]
    internal static partial uint* GetSidSubAuthority(nint pSid, uint nSubAuthority);
}
