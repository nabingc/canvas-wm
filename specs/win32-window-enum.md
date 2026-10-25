# Spec: win32-window-enum

## Goal

Implement the Engine's OS seam `IWindowSource` (specs/window-tracker.md) for
Windows, in `CanvasWm.Win32`:

- `Enumerate()`: every top-level window, frontmost first, as `OsWindowFacts`,
  synchronously and without blocking on hung applications.
- `Describe(handle)`: the same facts for one window, or null.
- `Subscribe(handler)`: `SetWinEventHook` notifications mapped to the 12
  `RawWindowEventKind` values and delivered later on the engine thread through a
  dispatch delegate that the App composition (#29) supplies.

The adapter **reports facts only**. It contains no minimum size, no
exclusion list, no removal decision, no de-duplication, no coalescing and no
timer. All of that already lives in `WindowClassifier` (Core) and
`WindowTracker` (Engine).

Out of scope:
- The message loop, the engine thread and the choice of UI framework (#29).
- Reading and writing frames for managed windows, the inverse DIP to pixel
  mapping and DWM border compensation (#11).
- Virtual-desktop identity (`IVirtualDesktopManager`) and desktop-switch
  detection (#24/#29).
- Pointer-drag brackets (`EVENT_SYSTEM_MOVESIZESTART/END`), which belong to
  the native-drag feature.
- Live capture (#16).

## Placement

- **Project:** `src/CanvasWm.Win32` (`net10.0-windows`). It already
  references Core and Engine (solution-skeleton). Changes to the csproj:
  - `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`. Source-generated
    `LibraryImport` code and function pointers need it.
  - `<ItemGroup><InternalsVisibleTo Include="CanvasWm.Win32.Tests" /></ItemGroup>`.
  - **No** `PackageReference` (A1).
- **Namespaces and files:**
  - `CanvasWm.Win32.Coordinates`: `PixelRect.cs`, `DipSpace.cs` (public).
  - `CanvasWm.Win32.Windows`:
    - `Win32WindowSource.cs` and `HandleConversion.cs` (public);
    - `FactGatherer.cs`, `FactMapping.cs`, `WinEventMapping.cs`,
      `EventPump.cs` and `ProcessInfoCache.cs` (internal).
  - `CanvasWm.Win32.Interop`: `NativeMethods.*.cs` (internal static partial
    class, one file per DLL: User32, Dwmapi, Kernel32, Advapi32, Shcore),
    `NativeConstants.cs` and `NativeStructs.cs` (all internal).
- **Tests:**
  - `tests/CanvasWm.Win32.Tests/Windows/*Tests.cs` and
    `tests/CanvasWm.Win32.Tests/Coordinates/*Tests.cs`, in namespaces
    `CanvasWm.Win32.Tests.Windows` and `CanvasWm.Win32.Tests.Coordinates`.
  - Test helpers go in `tests/CanvasWm.Win32.Tests/Support/`:
    - `MessageLoopThread`;
    - `ProbeProcess` (an out-of-process test window);
    - test-only P/Invoke declarations.
  - These tests run only on Windows (Win32.Tests `IsTestProject` is false
    off-Windows, solution-skeleton B12).
- **Manual checklist:** `windows-verification/win32-window-enum.md`. It
  contains every AC marked [windows-manual], with the steps and expected
  results written here.
- **Reuses, unchanged:**
  - `CanvasWm.Core.Windows.OsWindowFacts` and `CloakKind`;
  - `CanvasWm.Core.Geometry.Rect`;
  - `CanvasWm.Engine.Services.IWindowSource`, `RawWindowEvent` and
    `RawWindowEventKind`.
- **No file in Core, Engine, Core.Tests, Engine.Tests or
  `Directory.Build.props` changes.**
- Coordinates handed out are DIPs, y-down, in one virtual-desktop space
  whose origin is the primary monitor's top-left (B10).

## Public API

All public members carry XML docs (build rule). A null non-nullable reference
argument throws `ArgumentNullException`.

```csharp
namespace CanvasWm.Win32.Coordinates;

/// <summary>A rectangle in physical (device) pixels of the Windows virtual screen, as Win32
/// RECT: Right and Bottom are exclusive. No validation; Width/Height may be negative.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Right - Left.</summary>
    public int Width { get; }
    /// <summary>Bottom - Top.</summary>
    public int Height { get; }
}

/// <summary>The single mapping between physical pixels and CanvasWM DIPs (B10). Pure.
/// Every Win32 feature (#10 facts, #11 frame writes, #29 display frames) must use it.</summary>
public static class DipSpace
{
    /// <summary>96: the DPI at which one physical pixel equals one DIP.</summary>
    public const double BaseDpi = 96.0;

    /// <summary>dpi / 96.</summary>
    /// <exception cref="ArgumentOutOfRangeException">dpi is 0.</exception>
    public static double ScaleFor(uint dpi);

    /// <summary>Maps a physical rectangle to DIPs relative to the monitor it belongs to:
    /// X = M.Left + (r.Left - M.Left)/s, Y = M.Top + (r.Top - M.Top)/s, Width = r.Width/s,
    /// Height = r.Height/s, where M = monitorBounds and s = ScaleFor(monitorDpi). The monitor's
    /// own DIP frame is ToDip(M, M, dpi) = (M.Left, M.Top, M.Width/s, M.Height/s).</summary>
    /// <exception cref="ArgumentOutOfRangeException">monitorDpi is 0.</exception>
    public static Rect ToDip(PixelRect pixels, PixelRect monitorBounds, uint monitorDpi);
}
```

```csharp
namespace CanvasWm.Win32.Windows;

/// <summary>Conversion between HWND values and the ulong handles used by OsWindowFacts and
/// RawWindowEvent. Bijective; never throws.</summary>
public static class HandleConversion
{
    /// <summary>unchecked((ulong)(long)hwnd).</summary>
    public static ulong ToHandle(nint hwnd);
    /// <summary>unchecked((nint)(long)handle).</summary>
    public static nint ToHwnd(ulong handle);
}

/// <summary>IWindowSource over user32/dwmapi. Reports facts only. Construct it on the engine
/// thread: that thread is the owner thread, must run a message loop, and is where hooks are
/// installed and events delivered.</summary>
public sealed class Win32WindowSource : IWindowSource, IDisposable
{
    /// <summary>Creates the source. Records the calling thread as the owner thread and reads
    /// CanvasWM's own process id and integrity level. Installs nothing.</summary>
    /// <param name="post">Schedules an action to run LATER on the owner thread, from its
    /// message loop, in FIFO order. It must never run the action inline (B17).</param>
    public Win32WindowSource(Action<Action> post);

    /// <inheritdoc/> (B2-B4)
    public IReadOnlyList<OsWindowFacts> Enumerate();

    /// <inheritdoc/> (B5)
    public OsWindowFacts? Describe(ulong handle);

    /// <summary>Starts delivering raw events to handler (B13-B18).</summary>
    /// <exception cref="InvalidOperationException">Called on a thread other than the owner
    /// thread.</exception>
    /// <exception cref="ObjectDisposedException">After Dispose.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The OS refused a hook
    /// (A16).</exception>
    public IDisposable Subscribe(Action<RawWindowEvent> handler);

    /// <summary>Ends every subscription and removes all hooks. Idempotent. Enumerate and
    /// Describe keep working afterwards.</summary>
    public void Dispose();
}
```

Internal surface. The names are binding because the tests use them through
`InternalsVisibleTo`:

```csharp
namespace CanvasWm.Win32.Windows;

internal readonly record struct StyleFacts(bool IsChild, bool HasCaption, bool IsResizable,
    bool HasMaximizeButton, bool IsAppWindow, bool IsToolWindow, bool IsNoActivate);

internal static class FactMapping
{
    internal static StyleFacts DecodeStyles(long style, long exStyle);              // B6
    internal static CloakKind ToCloakKind(bool readOk, uint cloaked);               // B7
    internal static bool? MaximizeButtonEnabled(bool readOk, uint maxButtonState);  // B8
    internal static bool IsFullScreen(PixelRect window, PixelRect monitor,
                                      bool hasCaption);                             // B9
    internal static bool IsShellClass(string className);                            // B6
    internal static string NormalizeAppId(string raw);                              // B11
    internal static bool IsAccessible(bool isOwnProcess, int? targetIntegrityRid,
                                      int ownIntegrityRid);                         // B12
    internal static string? AppNameFrom(string? fileDescription, string? imagePath); // B11
}

internal static class WinEventMapping
{
    /// <summary>The hooked event ranges, in installation order (B14).</summary>
    internal static IReadOnlyList<(uint Min, uint Max)> HookRanges { get; }
    /// <summary>Kind for a hooked event id, or null for any other id.</summary>
    internal static RawWindowEventKind? KindFor(uint eventId);
    /// <summary>B15 filter + handle choice. isWindow/rootOf are injected OS queries so the rule
    /// is testable without windows. Returns null to drop the event.</summary>
    internal static RawWindowEvent? Translate(uint eventId, nint hwnd, int idObject, int idChild,
                                              Func<nint, bool> isWindow, Func<nint, nint> rootOf);
}

/// <summary>FIFO queue between the hook callback and the handlers (B16-B18). No OS access.</summary>
internal sealed class EventPump
{
    internal EventPump(Action<Action> post);
    internal void Enqueue(RawWindowEvent e);
    internal IDisposable Add(Action<RawWindowEvent> handler);
    internal int HandlerCount { get; }
    internal int QueuedCount { get; }
}

// On Win32WindowSource, for tests: internal int ActiveHookCount { get; }
```

## Behaviour

### Interop

- **B1 (interop style).** Every native entry point is declared with
  `[LibraryImport]` (source-generated), and there is no `[DllImport]`. Rules:
  - strings are UTF-16 (`StringMarshalling.Utf16`, the `W` entry points);
  - every entry point whose failure is checked sets `SetLastError = true`;
  - native callbacks (`EnumWindows`, `SetWinEventHook`) use
    `[UnmanagedCallersOnly]` static methods passed as `delegate* unmanaged`
    function pointers, so there is no managed delegate that the GC could
    collect (B16);
  - no COM interop.

  The entry points used, and only these for fact gathering and events, are:
  - user32: `EnumWindows`, `IsWindow`, `GetAncestor`, `GetWindow`,
    `GetWindowLongPtrW`, `GetClassNameW`, `InternalGetWindowText`,
    `GetWindowThreadProcessId`, `IsWindowVisible`, `IsIconic`,
    `IsHungAppWindow`, `GetWindowRect`, `GetTitleBarInfo`, `GetShellWindow`,
    `FindWindowExW`, `MonitorFromWindow`, `GetMonitorInfoW`,
    `SetThreadDpiAwarenessContext`, `SetWinEventHook`, `UnhookWinEvent`;
  - dwmapi: `DwmGetWindowAttribute`;
  - shcore: `GetDpiForMonitor`;
  - kernel32: `OpenProcess`, `CloseHandle`, `QueryFullProcessImageNameW`,
    `GetApplicationUserModelId`, `GetProcessTimes`, `GetCurrentProcess`;
  - advapi32: `OpenProcessToken`, `GetTokenInformation`,
    `GetSidSubAuthorityCount`, `GetSidSubAuthority`.

  No entry point that sends a window message is used:
  - not `SendMessage*`;
  - not `GetWindowText*` (except `InternalGetWindowText`);
  - not `GetWindowTextLength`;
  - not `GetSystemMenu`.

  The `BCL System.Diagnostics.FileVersionInfo` is used for `AppName`.
  Constant values:

  | Name | Value | Name | Value |
  |---|---|---|---|
  | GWL_STYLE | -16 | GWL_EXSTYLE | -20 |
  | WS_CHILD | 0x40000000 | WS_CAPTION | 0x00C00000 |
  | WS_THICKFRAME | 0x00040000 | WS_MAXIMIZEBOX | 0x00010000 |
  | WS_EX_TOOLWINDOW | 0x00000080 | WS_EX_APPWINDOW | 0x00040000 |
  | WS_EX_NOACTIVATE | 0x08000000 | GW_OWNER | 4 |
  | GA_ROOT | 2 | MONITOR_DEFAULTTONEAREST | 2 |
  | MDT_EFFECTIVE_DPI | 0 | DWMWA_EXTENDED_FRAME_BOUNDS | 9 |
  | DWMWA_CLOAKED | 14 | DWM_CLOAKED_APP / SHELL / INHERITED | 1 / 2 / 4 |
  | STATE_SYSTEM_UNAVAILABLE | 0x1 | STATE_SYSTEM_INVISIBLE | 0x8000 |
  | PROCESS_QUERY_LIMITED_INFORMATION | 0x1000 | TOKEN_QUERY | 0x0008 |
  | TokenIntegrityLevel (class) | 25 | DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 | (nint)(-4) |
  | WINEVENT_OUTOFCONTEXT | 0x0000 | WINEVENT_SKIPOWNPROCESS | 0x0002 |
  | OBJID_WINDOW | 0 | CHILDID_SELF | 0 |

### Enumerate and Describe

- **B2 (Enumerate order).** `Enumerate()` has two phases:
  1. It calls `EnumWindows` once. The callback only appends each HWND to a
     list (passed via a `GCHandle` in `lParam`) and returns TRUE.
  2. After `EnumWindows` returns, it gathers facts (B4) for each HWND, in
     list order. `EnumWindows` order is z-order, topmost first.

  An HWND for which `IsWindow` is false at gathering time is omitted. The
  result is a new `List<OsWindowFacts>` each call (never null). It includes
  hidden, minimised, cloaked, own-process and shell windows. Duplicates
  cannot arise from `EnumWindows`, and none are added.
- **B3 (synchronous, hang-safe).** `Enumerate` and `Describe` complete
  synchronously on the calling thread. They:
  - send no window message (B1);
  - create no thread or timer;
  - never wait on another process's UI thread.

  A hung application therefore cannot block them. Expected cost is linear in
  the number of windows (A15 cache).
- **B4 (fact gathering for one HWND `h`).** All calls run inside a
  per-monitor-v2 DPI scope (B10.1). Every `OsWindowFacts` field is filled as
  follows. "Fails" means the API reports failure; the field then takes the
  stated fallback, and no exception escapes.

  | Field | Source | Fallback |
  |---|---|---|
  | `Handle` | `HandleConversion.ToHandle(h)` | (none) |
  | `ThreadId`, `ProcessId` | `GetWindowThreadProcessId(h, out pid)` (return value = thread id), cast `unchecked((int)…)` | 0 |
  | `IsOwnProcess` | `pid == Environment.ProcessId` | false |
  | `AppId`, `AppName` | B11 | null |
  | `Title` | `InternalGetWindowText(h, buf, 1024)`; the first n chars (A5) | `""` |
  | `Frame` | B10 | null |
  | `IsShellWindow` | `h == GetShellWindow()` or `IsShellClass(GetClassNameW(h))` | false |
  | `OwnsDesktop` | `GetShellWindow()` is non-zero and its pid equals `pid` | false |
  | `IsTopLevel` | `GetAncestor(h, GA_ROOT) == h` | false |
  | `IsOwned` | `GetWindow(h, GW_OWNER) != 0` | false |
  | `IsChild`, `HasCaption`, `IsResizable`, `HasMaximizeButton`, `IsAppWindow`, `IsToolWindow`, `IsNoActivate` | `DecodeStyles(GetWindowLongPtrW(h, GWL_STYLE), GetWindowLongPtrW(h, GWL_EXSTYLE))` (B6) | the bits read as 0 |
  | `MaximizeEnabled` | `GetTitleBarInfo` → B8 | null |
  | `IsVisible` | `IsWindowVisible(h)` | false |
  | `Cloak` | `DwmGetWindowAttribute(h, DWMWA_CLOAKED, &uint, 4)` → B7 | `None` |
  | `IsMinimized` | `IsIconic(h)` | false |
  | `IsFullScreen` | B9 | false |
  | `IsHung` | `IsHungAppWindow(h)` | false |
  | `IsAccessible` | B12 | false |

- **B5 (Describe).**
  - `Describe(0)` returns null.
  - Otherwise, with `h = ToHwnd(handle)`: if `IsWindow(h)` is false, it
    returns null. If not, it returns the B4 facts for `h`.
  - It works for any window, including child windows, which report
    `IsTopLevel = false`.
  - Describe never throws for OS failures.

### Pure mappings (FactMapping)

- **B6 (styles and classes).** `DecodeStyles(style, ex)`:
  - `IsChild = (style & WS_CHILD) != 0`;
  - `HasCaption = (style & WS_CAPTION) == WS_CAPTION`, so both bits are
    required;
  - `IsResizable = (style & WS_THICKFRAME) != 0`;
  - `HasMaximizeButton = (style & WS_MAXIMIZEBOX) != 0`;
  - `IsToolWindow = (ex & WS_EX_TOOLWINDOW) != 0`;
  - `IsAppWindow = (ex & WS_EX_APPWINDOW) != 0`;
  - `IsNoActivate = (ex & WS_EX_NOACTIVATE) != 0`.

  `IsShellClass(c)` is true iff `c` is exactly (ordinal) one of `Progman`,
  `WorkerW`, `Shell_TrayWnd` or `Shell_SecondaryTrayWnd`.
- **B7 (cloak).** `ToCloakKind(readOk, v)`:
  - `!readOk` or `v == 0` → `None`;
  - else if `v & 2` → `Shell`;
  - else if `v & 1` → `App`;
  - else if `v & 4` → `Inherited`;
  - else (only unknown bits) → `App` (A10).
- **B8 (maximize enabled).** `GetTitleBarInfo(h, &tbi)` with
  `tbi.cbSize = sizeof(TITLEBARINFO)`, and `state = tbi.rgstate[3]` (the
  maximize button). `MaximizeButtonEnabled(readOk, state)`:
  - `!readOk` → null;
  - `state & STATE_SYSTEM_INVISIBLE` → null (no visible button; whether the
    button exists is already reported by `HasMaximizeButton`);
  - `state & STATE_SYSTEM_UNAVAILABLE` → false;
  - else → true.

  A6 explains why `GetSystemMenu` is not used.
- **B9 (full screen).** `IsFullScreen(w, m, hasCaption)` is
  `!hasCaption && w.Left <= m.Left && w.Top <= m.Top &&
  w.Right >= m.Right && w.Bottom >= m.Bottom`. The inputs are:
  - `w` = `GetWindowRect(h)` in physical pixels, under the B10.1 scope;
  - `m` = `rcMonitor` of `MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST)`.

  The zoomed (maximised) state is not an input (A18). A normal maximised
  window has a caption, so it is not full screen. If the window rect or
  monitor cannot be read, the result is false. The comparison is exact on
  integers, with no tolerance.

### Coordinates

- **B10 (frame in DIPs).**
  1. **DPI scope.** Each public `Enumerate`, `Describe` and hook-callback
     body runs between
     `old = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)`
     and `SetThreadDpiAwarenessContext(old)` (in a `finally`). All
     coordinate APIs therefore return physical pixels, whatever the
     process manifest says (A9).
  2. **Physical frame.** `DwmGetWindowAttribute(h,
     DWMWA_EXTENDED_FRAME_BOUNDS, &RECT, 16)`. If the HRESULT is not
     S_OK, use `GetWindowRect(h)`. If both fail, `Frame = null`.
  3. **Monitor.** `hm = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST)`,
     `M = GetMonitorInfoW(hm).rcMonitor` and
     `dpi = GetDpiForMonitor(hm, MDT_EFFECTIVE_DPI).dpiX`. The window's own
     DPI (`GetDpiForWindow`) is not used: it reflects the target app's DPI
     awareness, not the monitor's (A8). If any of these fails or
     `dpi == 0`, then `Frame = null`.
  4. `Frame = DipSpace.ToDip(physical, M, dpi)`, with no rounding.
  5. **Space.** The physical virtual screen has its origin at the primary
     monitor's top-left and is y-down. `DipSpace` keeps each monitor's
     top-left at its physical coordinates and scales only within the
     monitor. So every monitor's DIP frame starts where its pixels start,
     and frames never overlap when every scale is at least 1. Gaps may
     appear to the right of and below monitors with scale > 1 (A8).
  6. Minimised windows report whatever bounds the OS returns (typically
     around (−32000, −32000)), mapped through their nearest monitor. The
     classifier ignores minimised windows.
- **B10a (`DipSpace` arithmetic).** `ScaleFor(d) = d / 96.0`. For `ToDip`,
  see the formula in the Public API. All arithmetic is in `double`, with no
  clamping. `PixelRect.Width = Right - Left` and
  `PixelRect.Height = Bottom - Top`.

### Identity and access

- **B11 (AppId, AppName).**
  1. **App process.** `ap = pid`. If `GetClassNameW(h) ==
     "ApplicationFrameWindow"`, walk its children with
     `FindWindowExW(h, after, "Windows.UI.Core.CoreWindow", null)`. If a
     child's pid differs from `pid`, that child's pid becomes `ap`; the
     first such child wins. `ProcessId` in the facts **stays `pid`**
     (A4).
  2. **Process info.** Open `ap` with
     `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, ap)`. If that
     fails, `AppId`, `AppName` and the integrity level (B12) are unknown.
     Otherwise:
     - `GetApplicationUserModelId(hProc, ref len, buf)`. When it returns
       `ERROR_SUCCESS` (0), `AppId = NormalizeAppId(aumid)`. Use a buffer of
       `APPLICATION_USER_MODEL_ID_MAX_LENGTH` (130) and retry once with the
       returned length if it reports `ERROR_INSUFFICIENT_BUFFER`.
     - Otherwise `QueryFullProcessImageNameW(hProc, 0, buf, ref size)`,
       with a 1024-char buffer. On success, `AppId = NormalizeAppId(path)`.
     - `AppName = AppNameFrom(FileVersionInfo.GetVersionInfo(path).FileDescription,
       path)`. If `GetVersionInfo` throws, `AppNameFrom(null, path)` is
       used. If no path is known, `AppName = null`.
  3. `NormalizeAppId(s) = s.ToLowerInvariant()`. This applies to both
     image paths and AUMIDs (A3; tracker A16).
  4. `AppNameFrom(desc, path)`:
     - `desc` if it is not null and not whitespace (trimmed);
     - else `Path.GetFileNameWithoutExtension(path)` if that is non-empty;
     - else null.

     The adapter never substitutes the tracker's `"App"` fallback.
  5. **Cache.** Results of step 2 (AppId, AppName and integrity RID) are
     cached in `ProcessInfoCache` keyed by `(ap, creationTime)`, where
     `creationTime` comes from `GetProcessTimes`. A reused pid therefore
     never gets stale data. At the end of each `Enumerate`, entries whose
     `ap` was not seen in that call are removed. There is no timer. If
     `GetProcessTimes` fails, nothing is cached for that call. Cache access
     is guarded by a lock.
- **B12 (accessible: UIPI).** On construction, the source reads its own
  token's integrity RID (`OpenProcessToken(GetCurrentProcess(),
  TOKEN_QUERY)` → `GetTokenInformation(TokenIntegrityLevel)` → the last
  sub-authority of the label SID). If that read fails, `ownRid = 0x2000`
  (medium) is used. For a target window, the RID of process `ap` is read
  the same way, from the handle of B11.2. `IsAccessible(isOwn, rid, own)`
  is:
  - `isOwn` → true;
  - `rid == null` (unreadable) → false;
  - else `rid <= own`.

  So an elevated (High, 0x3000) or System (0x4000) window is not accessible
  to a medium-integrity CanvasWM. It is accessible when CanvasWM itself runs
  elevated at the same or a higher level. uiAccess is ignored (A7).

### Events

- **B13 (subscription lifecycle).**
  - `Subscribe` checks the owner thread (`Environment.CurrentManagedThreadId`
    equals the constructor's thread), and that the source is not disposed.
  - When this is the first live subscription, it installs the hooks (B14).
  - It returns a subscription whose `Dispose()`:
    - removes the handler at once, so no event is delivered to it
      afterwards, even if one is already queued;
    - is idempotent;
    - when the last live subscription goes, removes every hook with
      `UnhookWinEvent`.
  - `Dispose()` on another thread removes the handler immediately and passes
    the unhook step to `post` (`UnhookWinEvent` must run on the installing
    thread).
  - `Win32WindowSource.Dispose()` disposes every subscription in the same
    way and marks the source disposed.
  - `ActiveHookCount` is the number of installed hooks: 0 or 6.
- **B14 (hooks).** One `SetWinEventHook(min, max, 0, &Callback, 0, 0,
  WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)` is installed per range
  in `HookRanges`, in this order:

  | # | Range | Events → kind |
  |---|---|---|
  | 1 | 0x0003–0x0003 | EVENT_SYSTEM_FOREGROUND → Foreground |
  | 2 | 0x0016–0x0017 | MINIMIZESTART → Minimized; MINIMIZEEND → Restored |
  | 3 | 0x8000–0x8003 | OBJECT_CREATE → Created; DESTROY → Destroyed; SHOW → Shown; HIDE → Hidden |
  | 4 | 0x8005–0x8005 | OBJECT_FOCUS → FocusChanged |
  | 5 | 0x800B–0x800C | LOCATIONCHANGE → LocationChanged; NAMECHANGE → TitleChanged |
  | 6 | 0x8017–0x8018 | OBJECT_CLOAKED → Cloaked; UNCLOAKED → Uncloaked |

  `KindFor` returns exactly these 12 pairs and null for every other id. If
  any `SetWinEventHook` returns 0, the hooks already installed are removed
  and `Subscribe` throws `Win32Exception(lastError)` (A16). Each hook handle
  is registered in a static, lock-guarded map from hook to source **after**
  `SetWinEventHook` returns and before `Subscribe` returns. Out-of-context
  callbacks are only dispatched when the owner thread pumps messages, so
  nothing is lost.
- **B15 (filter and handle choice).** `Translate(ev, hwnd, idObject,
  idChild, isWindow, rootOf)`:
  1. `k = KindFor(ev)`. If it is null, drop the event.
  2. **Foreground and FocusChanged.** No `idObject`/`idChild` filter.
     - `hwnd == 0` → `(k, 0)`.
     - Otherwise `r = rootOf(hwnd)`, and the result is `(k, ToHandle(r != 0
       ? r : hwnd))`.

     A focus event on a control is thus reported for its top-level window.
  3. **All other kinds.**
     - Drop the event unless `idObject == OBJID_WINDOW && idChild ==
       CHILDID_SELF && hwnd != 0`.
     - Drop it if `isWindow(hwnd) && rootOf(hwnd) != hwnd` (a live
       non-top-level window).
     - Otherwise the result is `(k, ToHandle(hwnd))`.

     A window that no longer exists, for example at Destroyed or at a Hidden
     just before destruction, is delivered without the top-level check,
     because that check cannot be made (A11).

  In production, `isWindow = IsWindow` and `rootOf = h => GetAncestor(h,
  GA_ROOT)`, evaluated inside the callback.
- **B16 (callback).** The single `[UnmanagedCallersOnly]` static callback:
  - looks up the source by hook handle (unknown hook → return);
  - applies `Translate`;
  - calls `pump.Enqueue(e)` for a non-null result;
  - returns.

  It never calls a handler. It catches every exception and discards the
  event, because exceptions must not cross the native boundary (A17). There
  is no managed delegate to keep alive: the function pointer refers to a
  static method, and the hook map holds the source strongly until unhook.
- **B17 (EventPump: delivery).**
  - `Enqueue(e)` appends `e` to a FIFO queue. If no drain is pending or
    running, it marks a drain pending and calls `post(Drain)`.
  - `Drain`:
    - clears the pending mark and sets `running`;
    - repeatedly dequeues the oldest event and invokes every handler that
      is live at that moment, in `Add` order, until the queue is empty;
    - then clears `running`.
  - Events enqueued during a drain are delivered by the same drain,
    preserving FIFO order. A `Drain` invoked while another drain is running
    on the stack (a handler that pumps messages) returns at once without
    delivering anything. Handlers therefore never nest.
  - If `post` runs `Drain` inline, during `Enqueue`, `Drain` throws
    `InvalidOperationException` and the event stays queued. In production
    the callback (B16) discards that exception. This is the detectable form
    of a contract violation by #29.
  - If a handler throws:
    - the exception propagates out of `Drain` to the message loop;
    - the event being delivered is not re-delivered;
    - if events remain queued, a new drain is posted before the exception
      propagates.
  - Events are delivered in OS callback order. There is no de-duplication,
    no coalescing (LocationChanged included, A12) and no delay beyond the
    `post` hop.
- **B18 (no inline delivery).** As a consequence of B16 and B17, no handler
  ever runs inside `Enumerate`, `Describe`, `Subscribe`, a subscription's
  `Dispose`, or the hook callback. This is the `IWindowSource` contract
  "never synchronously inside any IWindowSource call".

## Edge cases & errors

- **E1.** `new Win32WindowSource(null!)` and `Subscribe(null!)` throw
  `ArgumentNullException`. `Describe(0)` returns null and does not throw.
- **E2.** A window destroyed between `EnumWindows` and fact gathering is
  omitted (B2). A window destroyed during gathering gives facts that are
  partly default; the tracker re-checks it later.
- **E3.** Windows on a secure desktop (UAC prompt, lock screen) are not
  enumerated by `EnumWindows`. This is not an error.
- **E4.** Packaged app frames without a hosted `CoreWindow` child (for
  example while the app is suspended or still starting) report the AppId of
  `ApplicationFrameHost.exe`'s path. Once the child appears, a later
  `Describe` reports the AUMID.
- **E5.** `OpenProcess` fails for protected processes. Their windows then
  have `AppId = AppName = null` and `IsAccessible = false`.
- **E6.** Title longer than 1023 UTF-16 units: truncated (A5).
- **E7.** `Subscribe` while a previous subscription is live does not install
  a second set of hooks (`ActiveHookCount` stays 6). Each event reaches
  every live handler.
- **E8.** Disposing a subscription inside its own handler stops further
  delivery to it, including the remaining events of the current drain.
- **E9.** `Subscribe` after `Dispose` throws `ObjectDisposedException`.
  `Enumerate` and `Describe` after `Dispose` still work.
- **E10.** Events of CanvasWM's own process are not delivered
  (`WINEVENT_SKIPOWNPROCESS`). Own windows still appear in `Enumerate`, with
  `IsOwnProcess = true`.

## Acceptance criteria

Legend:
- [mac]: verified on the Mac by a command or by inspection. This feature
  has no Mac xUnit tests (A14).
- [windows]: an automated xUnit test in `tests/CanvasWm.Win32.Tests` that
  runs with `dotnet test tests/CanvasWm.Win32.Tests` in an interactive
  Windows session.
- [windows-manual]: an item in `windows-verification/win32-window-enum.md`.

Manual items use the probe tests from AC33.

### Mac (build and inspection)

- **AC1** [mac] `dotnet build CanvasWm.slnx` exits 0 with 0 warnings and 0
  errors. `dotnet test tests/CanvasWm.Core.Tests` and `dotnet test
  tests/CanvasWm.Engine.Tests` still pass with 0 failed.
- **AC2** [mac] File changes:
  - `git status` lists changes only under `src/CanvasWm.Win32/`,
    `tests/CanvasWm.Win32.Tests/` and
    `windows-verification/win32-window-enum.md`;
  - no file in `src/CanvasWm.Core`, `src/CanvasWm.Engine`,
    `tests/CanvasWm.Core.Tests`, `tests/CanvasWm.Engine.Tests` or
    `Directory.Build.props` changes.
- **AC3** [mac] `CanvasWm.Win32.csproj`:
  - references exactly Core and Engine;
  - has no `PackageReference`;
  - sets `AllowUnsafeBlocks` true;
  - declares `InternalsVisibleTo` `CanvasWm.Win32.Tests`.

  `CanvasWm.Win32.Tests.csproj` gains no package other than the existing
  three. (Placement; A1)
- **AC4** [mac] No policy in Win32. Each command below, run from the repo
  root, prints nothing:
  - `rg -n "ClassifierPolicy|WindowClassifier|TrackerOptions|WindowTracker|IgnoreReason|TrackedFate|ExcludedApps|MinimumSize" src/CanvasWm.Win32` (`WindowKey` dropped by #11 R3b: `Win32WindowSystem` implements `IWindowSystem`)
  - `rg -nP "(?<![\w.])(120|80|0\.05|0\.4|3\.0|0\.3|0\.6)(?![\w.])" src/CanvasWm.Win32 -g "*.cs"`
  - `rg -n "System\.Threading\.Timer|PeriodicTimer|Task\.Delay|Thread\.Sleep|Stopwatch|DispatcherTimer|SetTimer" src/CanvasWm.Win32`

  (B3, B17; CLAUDE.md "Win32 adapts the OS")
- **AC5** [mac] Interop hygiene. Each of these prints nothing:
  - `rg -n "DllImport" src/CanvasWm.Win32`
  - `rg -nP "SendMessage|(?<!Internal)GetWindowText|GetSystemMenu|SHGetPropertyStoreForWindow|ComImport" src/CanvasWm.Win32`

  `rg -c "LibraryImport" src/CanvasWm.Win32` is ≥ 1. (B1, B3)
- **AC6** [mac] `rg -n "TrellisWM|Licensing" src tests` prints nothing.
  (CLAUDE.md)

### Windows: pure helpers (automated, no window needed)

- **AC7** [windows] `HandleConversion`:
  - `ToHwnd(ToHandle(x)) == x` for x ∈ {0, 1, 0x10010, nint.MaxValue,
    nint.MinValue, −1};
  - `ToHandle(-1) == ulong.MaxValue`;
  - `ToHandle(ToHwnd(u)) == u` for u ∈ {0, 4211, ulong.MaxValue}.
- **AC8** [windows] `DipSpace` (tolerance 1e-9):
  - `ScaleFor(96)==1`, `ScaleFor(144)==1.5`, `ScaleFor(192)==2`, and
    `ScaleFor(0)` throws `ArgumentOutOfRangeException`.
  - Monitor M = (0,0,3840,2160) at 144 dpi:
    - `ToDip(M,M,144) == (0,0,2560,1440)`;
    - `ToDip((300,150,1800,1050), M, 144) == (200,100,1000,600)`.
  - Monitor N = (3840,0,5760,1080) at 96 dpi:
    `ToDip((4000,100,4800,700), N, 96) == (4000,100,800,600)`.
  - Monitor L = (−3840,0,0,2160) at 192 dpi:
    - `ToDip(L,L,192) == (−3840,0,1920,1080)`;
    - `ToDip((−3640,200,−2640,1200), L, 192) == (−3740,100,500,500)`.
  - Non-overlap: monitors (0,0,1920,1080)@96 and (1920,0,5760,2160)@192 map
    to DIP frames (0,0,1920,1080) and (1920,0,1920,1080).
  - A rect with Right < Left maps to a negative width without throwing.

  (B10, B10a)
- **AC9** [windows] `DecodeStyles` truth table:
  - `WS_OVERLAPPEDWINDOW` (0x00CF0000), ex 0 →
    `HasCaption, IsResizable, HasMaximizeButton` true, the rest false;
  - style `WS_CAPTION` only → `HasCaption` true, `IsResizable` false;
  - style `WS_BORDER` (0x00800000) alone → `HasCaption` false;
  - `WS_CHILD` → `IsChild`;
  - ex `0x80 | 0x40000 | 0x08000000` → `IsToolWindow`, `IsAppWindow` and
    `IsNoActivate` true.

  `IsShellClass`:
  - true for the four names;
  - false for `"progman"` (ordinal), `"Notepad"`, `""` and
    `"ApplicationFrameWindow"`.

  (B6)
- **AC10** [windows] `ToCloakKind`:
  - `(false,2)→None`, `(true,0)→None`, `(true,2)→Shell`, `(true,1)→App`,
    `(true,4)→Inherited`;
  - `(true,3)→Shell`, `(true,5)→App`, `(true,6)→Shell`;
  - `(true,8)→App`.

  `MaximizeButtonEnabled`:
  - `(false,0)→null`, `(true,0)→true`, `(true,0x1)→false`,
    `(true,0x8000)→null`, `(true,0x8001)→null`;
  - `(true,0x40)→true` (other state bits are ignored).

  (B7, B8)
- **AC11** [windows] `IsFullScreen`, with m = (0,0,1920,1080):
  - w = m, no caption, not zoomed → true;
  - w = (−8,−8,1928,1088), no caption → true (covers);
  - w = m, with caption → false;
  - w = (−8,−8,1936,1056), with caption (normal maximised) → false;
  - w = (0,0,1920,1040) → false;
  - w = (1,0,1920,1080) → false.

  (B9)
- **AC12** [windows] Identity helpers:
  - `NormalizeAppId("C:\\Program Files\\App\\X.EXE") == "c:\\program files\\app\\x.exe"`;
  - `NormalizeAppId("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App") == "microsoft.windowscalculator_8wekyb3d8bbwe!app"`;
  - `AppNameFrom(" Editor ", "c:\\a\\ed.exe") == "Editor"`;
  - `AppNameFrom("  ", "c:\\a\\ed.exe") == "ed"`;
  - `AppNameFrom(null, null) == null`;
  - `AppNameFrom(null, "") == null`.

  `IsAccessible`:
  - `(true,null,0x2000)→true`;
  - `(false,null,0x2000)→false`;
  - `(false,0x2000,0x2000)→true`;
  - `(false,0x1000,0x2000)→true`;
  - `(false,0x3000,0x2000)→false`;
  - `(false,0x3000,0x3000)→true`;
  - `(false,0x4000,0x3000)→false`.

  (B11, B12)
- **AC13** [windows] `WinEventMapping`:
  - `HookRanges` equals the six ranges of B14, in that order;
  - `KindFor` maps exactly the 12 ids of B14;
  - `KindFor` is null for 0x0001, 0x0015, 0x0018, 0x8004, 0x8006, 0x800A,
    0x800D, 0x800E, 0x8016 and 0x8019.

  `Translate` with fakes, where `isWindow` is true for {10, 11} and
  `rootOf(10)=10`, `rootOf(11)=10`, `rootOf(other)=0`:
  - (CREATE,10,0,0) → (Created,10);
  - (CREATE,11,0,0) → null (live child);
  - (DESTROY,99,0,0) → (Destroyed,99) (dead window, no top-level check);
  - (SHOW,10,−4 /*OBJID_CLIENT*/,0) → null;
  - (NAMECHANGE,10,0,5) → null;
  - (LOCATIONCHANGE,0,0,0) → null;
  - (FOCUS,11,−4,3) → (FocusChanged,10);
  - (FOCUS,0,0,0) → (FocusChanged,0);
  - (FOCUS,99,0,0) → (FocusChanged,99) (root unknown);
  - (FOREGROUND,10,0,0) → (Foreground,10);
  - (0x800E,10,0,0) → null.

  (B14, B15)
- **AC14** [windows] `EventPump`. The test `post` appends to a list `P` and
  does not run the action.
  - Two enqueues → `P.Count == 1` and no handler call. Running `P[0]`
    delivers both events in order to handlers h1 then h2 (`Add` order).
  - An enqueue made by h1 during the drain is delivered in the same drain,
    after the remaining events, and `P` does not grow.
  - A disposed handler gets nothing, including events already queued.
    Disposing twice is fine.
  - A nested `Drain` (h1 runs `P[0]` again) delivers nothing and returns.
  - With an inline post (`a => a()`), `Enqueue` throws
    `InvalidOperationException` and `QueuedCount == 1`.
  - h1 throws on event 1 of 3: the exception propagates from the drain, one
    new drain is posted, and running it delivers events 2 and 3.
  - `HandlerCount` and `QueuedCount` track these changes.

  (B17, B18)
- **AC15** [windows] Public surface by reflection over the `CanvasWm.Win32`
  assembly:
  - the public types are exactly `Win32WindowSource`, `HandleConversion`,
    `DipSpace` and `PixelRect`, plus the three added by #11 (see
    specs/win32-frame-writer.md AC12: `Win32WindowSystem`,
    `IManagedWindowMap`, `ManagedWindowIdentity`) — #11 R3c;
  - `Win32WindowSource` implements `IWindowSource` and `IDisposable`;
  - no type in the assembly has a field, property, method parameter or
    return type from `CanvasWm.Engine.Tracking` or of type
    `ClassifierPolicy`.

  (Public API; CLAUDE.md)

### Windows: real windows (automated)

Test support for AC16–AC27:
- `MessageLoopThread` is a dedicated thread that pumps messages
  (`MsgWaitForMultipleObjectsEx` + `PeekMessage`/`DispatchMessage`) and runs
  actions from a concurrent queue. Its `Post(Action)` enqueues and wakes the
  thread. The source is constructed and subscribed on this thread.
- `ProbeProcess` starts
  `powershell.exe -NoProfile -WindowStyle Hidden -Command` with an inline
  script. The script creates a `System.Windows.Forms.Form` titled
  `CanvasWmProbe-<guid>` at a requested position and size, prints its HWND,
  and runs `Application.Run`. Commands on stdin change the title, set
  `FormBorderStyle`/`MaximizeBox`, or hang the UI thread (A20).
- Event waits use a 5 s timeout. "Receives" means an event with the probe's
  handle and that kind arrives before the timeout. Expected sequences are
  checked as ordered subsequences; other events may interleave.

- **AC16** [windows] `Enumerate`:
  - It returns ≥ 1 facts, and every `Handle` is non-zero and distinct.
  - The handle sequence equals an independent `EnumWindows` capture taken
    immediately before, after removing handles for which `IsWindow` is
    false at comparison time. Allow a retry, up to 3 attempts, if the
    window set changed in between.
  - `GetShellWindow()`'s handle is present with `IsShellWindow` and
    `OwnsDesktop` true.
  - `FindWindowW("Shell_TrayWnd", null)`'s facts have `IsShellWindow` true.

  (B2, B4, B6)
- **AC17** [windows] In-process window. The test creates, on the loop
  thread, `CreateWindowExW(0, "Static", "CanvasWmSelf", WS_OVERLAPPEDWINDOW
  | WS_VISIBLE, x, y, 800, 600)` on the primary monitor. Expected
  `Describe` facts:
  - `IsOwnProcess`, `IsTopLevel` and `IsVisible` true;
  - `IsOwned` and `IsChild` false;
  - `HasCaption`, `IsResizable` and `HasMaximizeButton` true;
  - `MaximizeEnabled == true`;
  - `Title == "CanvasWmSelf"`;
  - `ThreadId` is the loop thread's native id, and `ProcessId ==
    Environment.ProcessId`;
  - `Frame` equals `DipSpace.ToDip(dwmBounds, rcMonitor, monitorDpi)`
    computed by the test from its own calls (tolerance 1e-9);
  - `IsAccessible` true;
  - `Cloak == None`;
  - `IsFullScreen` false.

  After `ShowWindow(SW_MINIMIZE)`, `IsMinimized` is true. After
  `ShowWindow(SW_MAXIMIZE)`, `IsFullScreen` is false. After `SW_HIDE`,
  `IsVisible` is false. The window appears in `Enumerate`. (B4, B5, B8, B9,
  B10)
- **AC18** [windows] Describe edge cases:
  - `Describe(0)` is null.
  - After `DestroyWindow`, `Describe(handle)` is null.
  - A child `Static` window (`WS_CHILD`) of the AC17 window has
    `IsTopLevel` false and `IsChild` true, and is absent from `Enumerate`.
  - An owned top-level window (created with the AC17 window as owner) has
    `IsOwned` true.
  - A window with `WS_EX_TOOLWINDOW` has `IsToolWindow` true.
  - A popup with `WS_POPUP` only and no caption covering the primary
    monitor's `rcMonitor` has `IsFullScreen` true.

  (B5, B6, B9)
- **AC19** [windows] Probe facts:
  - The probe window appears in `Enumerate` with `IsOwnProcess` false.
  - `AppId` equals the lower-cased full path of the probe process's
    `MainModule.FileName` (powershell.exe).
  - `AppName` equals that file's `FileDescription`, or `"powershell"` if
    that is empty.
  - `Title` starts with `CanvasWmProbe-`.
  - `IsAccessible` is true.
  - Its `ProcessId` and `ThreadId` match `GetWindowThreadProcessId`.

  (B4, B11, B12)
- **AC20** [windows] Probe events. Subscribe, then act on the probe from the
  test (P/Invoke in test code). Each action must receive the listed kinds
  for the probe handle:

  | Action | Receives |
  |---|---|
  | `ShowWindow(SW_MINIMIZE)` | Minimized |
  | `ShowWindow(SW_RESTORE)` | Restored |
  | `SetWindowPos` move by 50 px | LocationChanged |
  | stdin "title X" | TitleChanged; `Describe` then has `Title == "X"` |
  | `ShowWindow(SW_HIDE)`, then `SW_SHOW` | Hidden, then Shown |
  | start a second probe | Created, then Shown (second handle) |
  | `PostMessage(WM_CLOSE)` | Destroyed |

  No handler call happens synchronously inside the test's `Enumerate` or
  `Describe` calls: a flag set around them is never observed true in the
  handler. All handler calls occur on the loop thread. (B13–B18)
- **AC21** [windows] Own process is skipped. With a live subscription,
  minimising, moving and retitling the AC17 in-process window produce no
  event carrying its handle within 1 s. The probe in the same test still
  produces events. (B14, E10)
- **AC22** [windows] Filter:
  - Creating and moving a child window of the probe (stdin command "child")
    delivers no Created or LocationChanged for the child handle.
  - Clicking into the probe's child control via `SetFocus` in the probe
    (stdin "focus") delivers FocusChanged with the probe's top-level handle.

  (B15)
- **AC23** [windows] Subscription lifecycle:
  - `ActiveHookCount` is 0 before Subscribe, 6 after the first and second
    Subscribe, 6 after disposing one, and 0 after disposing both.
  - After the last dispose, probe actions deliver nothing for 1 s.
  - A subscription disposed on another thread stops delivery immediately.
    `ActiveHookCount` reaches 0 after the loop thread runs the posted
    unhook.
  - `Subscribe` from a non-loop thread throws `InvalidOperationException`.
  - After `Win32WindowSource.Dispose()`, `Subscribe` throws
    `ObjectDisposedException`, and `Enumerate` still returns facts.

  (B13, E7–E9)
- **AC24** [windows] GC safety. Subscribe, then call
  `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect()` three times
  on the loop thread. Probe minimise and restore still deliver Minimized and
  Restored. (B1, B16)
- **AC25** [windows] Hung app:
  - The probe is told "hang" and its UI thread sleeps for 15 s.
  - After waiting 6 s, three consecutive `Enumerate` calls each complete in
    < 1000 ms (measured by the test).
  - The probe's facts then have `IsHung == true`, and `Title` is still the
    probe title.
  - `Describe(probe)` completes in < 200 ms.

  (B3, B4; findings H5)
- **AC26** [windows] Fixed-size window facts. Probe stdin "fixed" sets
  `FormBorderStyle = FixedDialog` and `MaximizeBox = false`. After that:
  - `IsResizable` and `HasMaximizeButton` are false;
  - `MaximizeEnabled` is false (minimise box present, maximise button shown
    disabled) or null (button invisible). Either outcome passes, and the
    test records which one occurred.

  (B6, B8)
- **AC27** [windows] DPI scope:
  - The test thread's DPI awareness context is set to `UNAWARE` before
    calling `Describe` on the AC17 window. The resulting `Frame` equals the
    one obtained under per-monitor-v2 (tolerance 1e-9).
  - After the call, the thread's awareness context is `UNAWARE` again
    (restored).

  (B10.1)

### Windows: manual checklist

AC33 provides the probe tool that the manual steps use:
- `CANVASWM_MANUAL=1 dotnet test tests/CanvasWm.Win32.Tests --filter
  "FullyQualifiedName~ManualProbe.DumpFacts"` writes every `Enumerate` fact
  to `%TEMP%\canvaswm-facts.tsv`, one row per window, with all 25 fields.
- `...~ManualProbe.RecordEvents` records events for `CANVASWM_PROBE_SECONDS`
  (default 30). It writes `%TEMP%\canvaswm-events.tsv`, with time, kind,
  handle and the `Describe` title.

- **AC28** [windows-manual] Classic app DIP frame:
  1. Set one monitor to 150 % scaling.
  2. Open Notepad (or another classic app on Windows 10) on that monitor.
  3. Measure its outer visible border with Snipping Tool, in physical px.
  4. Run DumpFacts.

  Expected: Notepad's row shows:
  - `Frame.Width` ≈ measured width / 1.5 and `Frame.Height` ≈ measured
    height / 1.5, within ±1 DIP;
  - `Frame.X - monitorLeft` ≈ (physical X − monitorLeft)/1.5;
  - `AppId` = its lower-cased exe path, and `AppName` = its file
    description (for example "Notepad");
  - `IsFullScreen` false.

  With a second monitor at 100 % to its right, a window moved there reports
  `Frame.X ≥` that monitor's physical left. (B10)
- **AC29** [windows-manual] Packaged apps:
  1. Open Calculator, which is UWP and hosted by ApplicationFrameHost.
  2. Run DumpFacts.

  Expected: the Calculator frame row has an `AppId` starting with
  `microsoft.windowscalculator_` and containing `!`, all lower case, and
  `ProcessId` is that of ApplicationFrameHost.exe (Task Manager → Details).
  On Windows 11, the new Notepad (a packaged desktop app) has
  `AppId = "microsoft.windowsnotepad_8wekyb3d8bbwe!app"`. (B11; A2–A4)
- **AC30** [windows-manual] Virtual desktops and cloaking:
  1. Create Desktop 2 (Win+Ctrl+D) and open a Notepad there.
  2. Switch back to Desktop 1 and run DumpFacts.
  3. Start RecordEvents and switch to Desktop 2 and back.

  Expected:
  - after step 2, that Notepad shows `Cloak = Shell` and `IsVisible =
    true`;
  - the event log has Uncloaked and Cloaked rows for its handle.

  A minimised window shows `IsMinimized = true`. (B7, B14)
- **AC31** [windows-manual] Elevated window:
  1. Run CanvasWM tests from a non-elevated terminal.
  2. Start `regedit` (accept UAC).
  3. Run DumpFacts.

  Expected: the Registry Editor row has `IsAccessible = false`. Repeat
  DumpFacts from an elevated terminal: `IsAccessible = true`. (B12)
- **AC32** [windows-manual] Focus, foreground and full screen. Start
  RecordEvents, then:
  - click between two Notepad windows → Foreground rows for each handle in
    click order, and FocusChanged rows with the same top-level handles;
  - click the desktop → a Foreground row whose handle is the Progman or
    WorkerW window (DumpFacts: `IsShellWindow = true`, `OwnsDesktop = true`);
  - press F11 in Edge or Chrome, then run DumpFacts → that window has
    `IsFullScreen = true`, both when F11 is pressed from a restored window
    and from a maximised one. After leaving F11 and maximising it,
    `IsFullScreen = false`;
  - minimise, restore and close a Notepad → Minimized, Restored, then
    Destroyed for its handle.

  (B9, B14, B15)
- **AC33** [windows-manual] Probe tool:
  - with `CANVASWM_MANUAL` unset, both ManualProbe tests pass in < 1 s and
    write nothing;
  - with it set, they produce the two TSV files described above, with a
    header row naming every `OsWindowFacts` field.

Count:
- **6 [mac]** (AC1–AC6);
- **27 [windows]**, of which **21 are automated** (AC7–AC27) and **6 are
  manual** (AC28–AC33).

## Assumptions

- **A1. Hand-written `LibraryImport`, not CsWin32.** *Needs human review.*
  About 35 entry points are needed. Writing them by hand:
  - adds no NuGet package, so solution-skeleton's package rules and
    `Directory.Build.props` stay as they are;
  - keeps the declarations small and readable, with no generated surface
    that warnings-as-errors and doc rules could trip on;
  - gives precise control of blittable signatures and
    `UnmanagedCallersOnly` callbacks.

  CsWin32 would help mainly for COM. This design needs no COM (A2).
  `LibraryImport` is in-box source generation, with no package.
- **A2. AUMID via `GetApplicationUserModelId` on the hosted process, not
  `SHGetPropertyStoreForWindow`.** *Needs human review.* This avoids COM.
  It covers UWP apps (through the `CoreWindow` child) and packaged desktop
  apps. It does **not** see a per-window AUMID that a classic app sets for
  taskbar grouping. Those windows get their exe path as `AppId`.
- **A3. AUMIDs are lower-cased too.** Package family names are
  case-insensitive on Windows. Lower-casing everything gives ordinal
  exclusion matching one canonical form (tracker A16).
- **A4. `ProcessId` of a UWP frame stays ApplicationFrameHost's pid.**
  *Needs human review.* The hosted `CoreWindow` can detach and re-attach.
  Using its pid would make the tracker's identity check (pid and thread)
  flip and cause spurious permanent removals. Consequence: all UWP windows
  share one pid for focus de-duplication, `FocusLeftWindows` and
  `AppActivated`. `AppId` still distinguishes the apps.
- **A5. Title via `InternalGetWindowText` with a 1024-unit buffer.** It
  sends no message and never blocks. It returns the text the window manager
  stores, which is what the taskbar shows. `SendMessageTimeout` would still
  stall for up to its timeout per hung window and adds a timeout constant.
  Titles longer than 1023 units are truncated.
- **A6. `MaximizeEnabled` from `GetTitleBarInfo`, not `GetSystemMenu`.**
  *Needs human review.* `GetSystemMenu(h, FALSE)` creates a private
  system-menu copy inside the foreign window, which is a side effect.
  `SC_MAXIMIZE`'s greyed state is also only refreshed when the menu is
  opened, so it goes stale after maximise and restore. The title-bar
  button state is computed by the OS from the current window state, sends
  no message, and gives a tri-state that matches the field's meaning
  (invisible → unknown).
- **A7. UIPI accessibility by integrity-level comparison.** An unreadable
  level counts as inaccessible, and uiAccess is ignored. Accessible iff
  target RID ≤ own RID. The fallback own RID is medium (0x2000).
- **A8. DIP space is anchor-preserving per monitor.** *Needs human review
  together with #11, #14 and #29.* Mixed-DPI desktops have no single linear
  DIP mapping. Keeping each monitor's physical top-left and scaling within
  it is the scheme some cross-platform toolkits use. It never overlaps for
  scales ≥ 1 and leaves gaps after scaled monitors. A window uses its
  OS-assigned nearest monitor (`MonitorFromWindow`) and that monitor's
  effective DPI. #11's inverse mapping must choose the same monitor. That
  is unambiguous inside monitors; for off-screen parked windows #11/#14 must
  define it (see "Reported for review").
- **A9. Per-call per-monitor-v2 thread DPI scope.** The adapter is correct
  even if the App manifest is missing or the test host is DPI-unaware.
  Minimum OS: Windows 10 version 1703 (per-monitor v2). A 64-bit process is
  assumed, although `HandleConversion` also round-trips on 32-bit.
- **A10. Cloak bit priority Shell > App > Inherited.** Unknown non-zero
  bits map to `App`, which the tracker treats as a temporary removal rather
  than "on another desktop". Shell wins when combined, so a suspended app on
  another desktop counts as "other desktop".
- **A11. Events for dead windows skip the top-level check.** Otherwise every
  Destroyed would be lost. The tracker ignores untracked handles without an
  OS call (tracker B13), so the extra child-window Destroyed events are
  cheap.
- **A12. No `LOCATIONCHANGE` coalescing** (orchestrator: no dedupe in
  Win32). The tracker's change-only filter handles bursts. If profiling
  shows load, coalescing would be a tracker or #29 decision.
- **A13. Threading via an injected `post` delegate and an owner thread
  equal to the constructing thread.** *Needs human review.* It is neutral
  with respect to WinForms, WPF or a raw loop. #29 passes `a =>
  ctx.Post(_ => a(), null)` or an equivalent. Out-of-context hooks already
  call back on the installing thread, and the post hop only defers delivery
  until after the callback returns.
- **A14. Pure helpers are tested on Windows, not on the Mac.** Win32 is a
  `-windows` assembly that the `net10.0` Mac test projects cannot reference.
  Linking its source files into a Mac test project would blur the
  architecture boundary. The helpers' tests are automated and need no
  desktop interaction. *Review:* if Mac coverage is wanted, a later change
  could source-link `FactMapping`, `WinEventMapping`, `EventPump` and
  `DipSpace` into a Mac test project.
- **A15. Per-process cache keyed by (pid, creation time), pruned per
  Enumerate.** It avoids reading version resources and tokens for every
  window every 3 s. It uses no timer.
- **A16. `Subscribe` throws `Win32Exception` if a hook cannot be
  installed.** A silent subscription would make the tracker blind with no
  signal. #29 decides how to report it.
- **A17. Exceptions inside the native callback are swallowed** and the event
  is dropped. Letting them through would terminate the process. No logging
  seam exists yet.
- **A18. Full screen means "covers its monitor" with no caption**, not
  exact equality. Some games and players size slightly past the monitor.
  The zoomed state is deliberately ignored. Revised 2026-10-25 after Windows
  verification: Chrome in F11 entered from a maximised window keeps
  `WS_MAXIMIZE` (`IsZoomed` true) while having no caption and a rect equal
  to `rcMonitor`, so the earlier "never for a zoomed window" rule reported
  it as not full screen. A normal maximised Chrome has `WS_CAPTION` and stops
  at the work area, so the caption test alone excludes it. Accepted edge
  case: a captionless app maximised over an auto-hide taskbar counts as full
  screen.
- **A19. `IsShellWindow` also includes `GetShellWindow()` by handle**, which
  is normally Progman anyway.
- **A20. Out-of-process test window via Windows PowerShell + WinForms.** It
  is present on every Windows 10/11 machine and needs no extra build
  artefact. It is slow to start (about 2 s), so it is started once per test
  class.

## Reported for review (no earlier spec changed)

- **DIP mapping ownership.** `DipSpace` (A8) is public in Win32 so that
  #11, #14 and #29 reuse it. `DisplayFrames` (tracker) and display work
  areas must be computed with `ToDip(M, M, dpi)`, or admission centre tests
  will disagree with window frames. #11 must add the inverse and define the
  monitor choice for off-screen DIP rects.
- **`OsWindowFacts.IsFullScreen` doc** (window-tracker spec) says "frame
  equals its monitor's full rect". This adapter uses "window rect covers the
  monitor, no caption" (A18). The Core doc wording could be
  relaxed. No code change is required.
- **UWP pid sharing** (A4) affects tracker focus de-duplication per pid.
  Acceptable for now. Revisit if focus reports for two UWP apps are
  swallowed.

## Traceability

| Spec item | Findings / earlier spec |
|---|---|
| B1, A1, A2, AC3, AC5 | Part 2 K (Win32 only adapts); orchestrator interop request |
| B2, B3, AC16, AC25 | H1 (EnumWindows, synchronous), H5 (hang safety), I7 (z-order); tracker A8 |
| B4, B5, AC17–AC19 | H2 fact table; tracker "Notes for #10"; OsWindowFacts docs |
| B6, AC9, AC18 | H2 (standard window, tool/app/noactivate, shell classes) |
| B7, AC10, AC30, A10 | H2 (visible/cloaked), H3 (cloak kinds) |
| B8, AC10, AC26, A6 | H2 (dockable = thick frame + maximize box + maximize enabled); rule 11 |
| B9, AC11, AC18, AC32, A18 | H2 (not full screen; maximised is not full screen); Windows notes |
| B10, B10a, AC8, AC17, AC27, AC28, A8, A9 | H2 (frame in DIPs); CLAUDE.md geometry; engine-interfaces coordinates |
| B11, AC12, AC19, AC29, A2–A4, A15 | H4 (AppId/AppName); tracker A16; Windows notes (packaged apps) |
| B12, AC12, AC31, A7 | H2 (size settable / UIPI); Windows notes (elevated windows) |
| B13, B14, AC13, AC20, AC21, AC23 | I1 (hooks, flags, unhook), I3 (event mapping); tracker #10 event list |
| B15, AC13, AC22, A11 | I2 (OBJID_WINDOW/CHILDID_SELF, top-level filter) |
| B16, AC24 | I1 (keep the callback alive) |
| B17, B18, AC14, AC20, A12, A13 | I1 (delivery on the UI thread), I4 (coalescing not done); Windows notes (re-entrant hooks); IWindowSource contract |
| AC4 | Part 2 K ("no thresholds, no exclusion logic, no temporary/permanent decision"); orchestrator scope |
| AC6 | CLAUDE.md clean-room boundary |
| J1–J2 (key format) | Not here: key stays in the tracker (`KeyFor`); HandleConversion only (AC7) |

## Amendments from #11 (win32-frame-writer, R3)

- C1: `DipSpace` gains `ToPixels` and a point `ToDip` (additive).
- AC4: `WindowKey` removed from the first pattern; AC15: public type list grows to seven.
- `NativeMethods` user32 doc reworded (C2); `FactGatherer` frame reading moves to `WindowGeometry.ReadDipFrame` with no behaviour change (C4).
