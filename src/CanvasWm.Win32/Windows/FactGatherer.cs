using System.Diagnostics;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Windows;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Windows;

/// <summary>Scope that switches the calling thread to per-monitor-v2 DPI awareness and restores
/// the previous context on dispose (B10.1).</summary>
internal readonly struct DpiScope : IDisposable
{
    private readonly nint _old;

    public DpiScope()
    {
        // SPEC-ASSUMPTION A9: per-call per-monitor-v2 thread DPI scope, whatever the manifest says.
        _old = NativeMethods.SetThreadDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }

    public void Dispose()
    {
        if (_old != 0)
        {
            NativeMethods.SetThreadDpiAwarenessContext(_old);
        }
    }
}

/// <summary>Reads OsWindowFacts for one HWND (B4). Reports facts only. The caller establishes the
/// DPI scope.</summary>
internal sealed unsafe class FactGatherer
{
    private const int TitleBufferChars = 1024;
    private const int ClassBufferChars = 256;
    private const int PathBufferChars = 1024;

    private readonly ProcessInfoCache _cache;
    private readonly int _ownProcessId;
    private readonly int _ownIntegrityRid;

    internal FactGatherer(ProcessInfoCache cache)
    {
        _cache = cache;
        _ownProcessId = Environment.ProcessId;
        _ownIntegrityRid = ReadIntegrityRid(NativeMethods.GetCurrentProcess()) ?? NativeConstants.MEDIUM_INTEGRITY_RID;
    }

    /// <summary>Gathers the facts for h. seen collects application pids for cache pruning.</summary>
    internal OsWindowFacts Gather(nint h, HashSet<int>? seen)
    {
        uint tid = NativeMethods.GetWindowThreadProcessId(h, out int pid);
        int threadId = unchecked((int)tid);
        if (tid == 0)
        {
            pid = 0;
        }

        string className = GetClassName(h);
        nint shell = NativeMethods.GetShellWindow();
        bool ownsDesktop = false;
        if (shell != 0)
        {
            NativeMethods.GetWindowThreadProcessId(shell, out int shellPid);
            ownsDesktop = shellPid == pid && pid != 0;
        }

        nint style = NativeMethods.GetWindowLongPtrW(h, NativeConstants.GWL_STYLE);
        nint exStyle = NativeMethods.GetWindowLongPtrW(h, NativeConstants.GWL_EXSTYLE);
        StyleFacts styles = FactMapping.DecodeStyles(style, exStyle);

        int ap = ResolveAppProcess(h, pid, className);
        ProcessInfo info = ReadProcessInfo(ap, seen);
        bool isOwn = pid == _ownProcessId;

        bool maxRead = ReadMaxButtonState(h, out uint maxState);
        bool cloakRead = NativeMethods.DwmGetWindowAttribute(h, NativeConstants.DWMWA_CLOAKED, out uint cloaked, sizeof(uint)) == 0;
        bool isZoomed = NativeMethods.IsZoomed(h);

        ReadGeometry(h, styles.HasCaption, isZoomed, out Rect? frame, out bool fullScreen);

        return new OsWindowFacts
        {
            Handle = HandleConversion.ToHandle(h),
            ProcessId = pid,
            ThreadId = threadId,
            AppId = info.AppId,
            AppName = info.AppName,
            Title = GetTitle(h),
            Frame = frame,
            IsOwnProcess = isOwn,
            IsShellWindow = h == shell || FactMapping.IsShellClass(className),
            OwnsDesktop = ownsDesktop,
            IsTopLevel = NativeMethods.GetAncestor(h, NativeConstants.GA_ROOT) == h,
            IsChild = styles.IsChild,
            IsOwned = NativeMethods.GetWindow(h, NativeConstants.GW_OWNER) != 0,
            IsAppWindow = styles.IsAppWindow,
            IsToolWindow = styles.IsToolWindow,
            IsNoActivate = styles.IsNoActivate,
            HasCaption = styles.HasCaption,
            IsResizable = styles.IsResizable,
            HasMaximizeButton = styles.HasMaximizeButton,
            MaximizeEnabled = FactMapping.MaximizeButtonEnabled(maxRead, maxState),
            IsVisible = NativeMethods.IsWindowVisible(h),
            Cloak = FactMapping.ToCloakKind(cloakRead, cloaked),
            IsMinimized = NativeMethods.IsIconic(h),
            IsFullScreen = fullScreen,
            IsHung = NativeMethods.IsHungAppWindow(h),
            IsAccessible = FactMapping.IsAccessible(isOwn, info.IntegrityRid, _ownIntegrityRid),
        };
    }

    private static string GetClassName(nint h)
    {
        char* buf = stackalloc char[ClassBufferChars];
        int n = NativeMethods.GetClassNameW(h, buf, ClassBufferChars);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    private static string GetTitle(nint h)
    {
        // SPEC-ASSUMPTION A5: InternalGetWindowText with a 1024-unit buffer; longer titles truncate.
        char* buf = stackalloc char[TitleBufferChars];
        int n = NativeMethods.InternalGetWindowText(h, buf, TitleBufferChars);
        return n > 0 ? new string(buf, 0, Math.Min(n, TitleBufferChars - 1)) : "";
    }

    private static bool ReadMaxButtonState(nint h, out uint state)
    {
        // SPEC-ASSUMPTION A6: maximize state from the title-bar button state, no system-menu access.
        var tbi = new NativeTitleBarInfo { cbSize = (uint)sizeof(NativeTitleBarInfo) };
        if (NativeMethods.GetTitleBarInfo(h, ref tbi))
        {
            state = tbi.rgstate[3];
            return true;
        }

        state = 0;
        return false;
    }

    private static void ReadGeometry(nint h, bool hasCaption, bool isZoomed, out Rect? frame, out bool fullScreen)
    {
        frame = null;
        fullScreen = false;

        bool haveWindowRect = NativeMethods.GetWindowRect(h, out NativeRect windowRect);

        PixelRect? physical = null;
        if (NativeMethods.DwmGetWindowAttributeRect(h, NativeConstants.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeRect dwm, (uint)sizeof(NativeRect)) == 0)
        {
            physical = dwm.ToPixelRect();
        }
        else if (haveWindowRect)
        {
            physical = windowRect.ToPixelRect();
        }

        nint hm = NativeMethods.MonitorFromWindow(h, NativeConstants.MONITOR_DEFAULTTONEAREST);
        if (hm == 0)
        {
            return;
        }

        var mi = new NativeMonitorInfo { cbSize = (uint)sizeof(NativeMonitorInfo) };
        if (!NativeMethods.GetMonitorInfoW(hm, ref mi))
        {
            return;
        }

        PixelRect monitor = mi.rcMonitor.ToPixelRect();
        if (haveWindowRect)
        {
            fullScreen = FactMapping.IsFullScreen(windowRect.ToPixelRect(), monitor, hasCaption, isZoomed);
        }

        // SPEC-ASSUMPTION A8: anchor-preserving per-monitor DIP mapping using the monitor's effective DPI.
        if (physical is { } p
            && NativeMethods.GetDpiForMonitor(hm, NativeConstants.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            && dpiX != 0)
        {
            frame = DipSpace.ToDip(p, monitor, dpiX);
        }
    }

    private static int ResolveAppProcess(nint h, int pid, string className)
    {
        // SPEC-ASSUMPTION A4: ProcessId stays the frame host's pid; only the app process used for identity changes.
        if (className != "ApplicationFrameWindow")
        {
            return pid;
        }

        nint after = 0;
        while (true)
        {
            nint child = NativeMethods.FindWindowExW(h, after, "Windows.UI.Core.CoreWindow", null);
            if (child == 0)
            {
                return pid;
            }

            NativeMethods.GetWindowThreadProcessId(child, out int childPid);
            if (childPid != 0 && childPid != pid)
            {
                return childPid;
            }

            after = child;
        }
    }

    private ProcessInfo ReadProcessInfo(int ap, HashSet<int>? seen)
    {
        seen?.Add(ap);
        var unknown = new ProcessInfo(null, null, null);
        if (ap <= 0)
        {
            return unknown;
        }

        nint hProc = NativeMethods.OpenProcess(NativeConstants.PROCESS_QUERY_LIMITED_INFORMATION, false, ap);
        if (hProc == 0)
        {
            return unknown;
        }

        try
        {
            bool haveTimes = NativeMethods.GetProcessTimes(hProc, out ulong created, out _, out _, out _);
            if (haveTimes && _cache.TryGet(ap, created, out ProcessInfo? cached) && cached is not null)
            {
                return cached;
            }

            string? aumid = QueryAumid(hProc);
            string? path = QueryImagePath(hProc);
            string? appId = aumid is not null ? FactMapping.NormalizeAppId(aumid)
                : path is not null ? FactMapping.NormalizeAppId(path)
                : null;

            string? appName = null;
            if (path is not null)
            {
                string? description = null;
                try
                {
                    description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                }
                catch (Exception)
                {
                    // Unreadable version resource: fall back to the file name.
                }

                appName = FactMapping.AppNameFrom(description, path);
            }

            var info = new ProcessInfo(appId, appName, ReadIntegrityRid(hProc));
            if (haveTimes)
            {
                _cache.Set(ap, created, info);
            }

            return info;
        }
        finally
        {
            NativeMethods.CloseHandle(hProc);
        }
    }

    private static string? QueryAumid(nint hProc)
    {
        // SPEC-ASSUMPTION A2: AUMID from the hosted process, no property-store COM.
        char* buf = stackalloc char[NativeConstants.APPLICATION_USER_MODEL_ID_MAX_LENGTH];
        uint len = NativeConstants.APPLICATION_USER_MODEL_ID_MAX_LENGTH;
        int rc = NativeMethods.GetApplicationUserModelId(hProc, ref len, buf);
        if (rc == NativeConstants.ERROR_SUCCESS)
        {
            return len > 1 ? new string(buf, 0, (int)len - 1) : null;
        }

        if (rc == NativeConstants.ERROR_INSUFFICIENT_BUFFER && len > NativeConstants.APPLICATION_USER_MODEL_ID_MAX_LENGTH && len <= 4096)
        {
            char[] big = new char[len];
            uint len2 = len;
            fixed (char* p = big)
            {
                rc = NativeMethods.GetApplicationUserModelId(hProc, ref len2, p);
            }

            if (rc == NativeConstants.ERROR_SUCCESS && len2 > 1)
            {
                return new string(big, 0, (int)len2 - 1);
            }
        }

        return null;
    }

    private static string? QueryImagePath(nint hProc)
    {
        char* buf = stackalloc char[PathBufferChars];
        uint size = PathBufferChars;
        return NativeMethods.QueryFullProcessImageNameW(hProc, 0, buf, ref size) && size > 0
            ? new string(buf, 0, (int)size)
            : null;
    }

    /// <summary>B12: the integrity RID (last sub-authority of the label SID) of a process, or null.</summary>
    private static int? ReadIntegrityRid(nint hProc)
    {
        if (!NativeMethods.OpenProcessToken(hProc, NativeConstants.TOKEN_QUERY, out nint token))
        {
            return null;
        }

        try
        {
            NativeMethods.GetTokenInformation(token, NativeConstants.TokenIntegrityLevel, null, 0, out uint needed);
            if (needed == 0 || needed > 1024)
            {
                return null;
            }

            byte* buf = stackalloc byte[(int)needed];
            if (!NativeMethods.GetTokenInformation(token, NativeConstants.TokenIntegrityLevel, buf, needed, out _))
            {
                return null;
            }

            nint sid = *(nint*)buf; // TOKEN_MANDATORY_LABEL.Label.Sid is the first field.
            if (sid == 0)
            {
                return null;
            }

            byte* count = NativeMethods.GetSidSubAuthorityCount(sid);
            if (count == null || *count == 0)
            {
                return null;
            }

            uint* rid = NativeMethods.GetSidSubAuthority(sid, (uint)(*count - 1));
            return rid == null ? null : unchecked((int)*rid);
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    /// <summary>Removes cache entries for application processes not seen in the last Enumerate.</summary>
    internal void Prune(HashSet<int> seen) => _cache.Prune(seen);
}
