# Spec: app-host

Feature #33 (split from #29 app-composition). Input: `findings/app-composition.md`
Part 2 (K, L, M4–M7, N1–N9, N13) plus the Part 1 rules it adapts (1–4, 22–25,
27–28), and the human decisions recorded on queue row #29 (WinForms; first
build single-desktop; do not wait for #14; display key = DISPLAYCONFIG device
path; data in `%LOCALAPPDATA%\CanvasWM`; layout files named by the SHA-256 of
the engine key; second launch signals the first instance and exits;
start-at-login off; tick = `DwmFlush` thread with a high-resolution timer
fallback).

## Goal

Produce the **first runnable Windows build**. `CanvasWm.App.exe` starts once
per user session, sits in the notification area, manages windows through the
existing engine router (#32), tracker (#30), window source (#10), frame writer
(#11) and hotkey source (#12), persists layouts, learned minimums and shortcut
settings, follows display changes, and on quit (or on a crash it can catch)
puts every window back on-screen before it exits.

Every decision that can be pure is pure and Mac-tested (JSON codecs, store
semantics over a file seam, clock scheduling, tick pacing, quit sequencing,
shortcut relay, display-key choice). Win32 gets thin OS adapters with no
policy. The App is wiring, WinForms tray and process lifetime only.

Out of scope: settings window, profile switching UI, exclusions UI,
start-at-login (#34; the option stays **off** and nothing is written to the
Run key); virtual desktops (#35; one engine per display with desktop id
`default`); the crash-safe parking journal (#14); overlays, renderers and
capture (#16); multi-display hand-off (#24); updates. Anything about licences,
locked states, trials, upgrades, onboarding or lessons is **excluded
entirely** — not modelled, not stubbed. No text in `src/` or `tests/` may
contain the words `TrellisWM` or `Licensing`.

## Placement

| Project | Namespace | New content |
|---|---|---|
| `CanvasWm.Core` | `CanvasWm.Core.Displays` (exists, #32) | `DisplayPathName`, `DisplayKeyChoice` (pure) |
| `CanvasWm.Engine` | `CanvasWm.Engine.Persistence` (new) | `ITextFileStore`, `TextFileStatus`, `TextFileRead`, `TextFileNames`, `DataFiles`, `SavedDisplayJson`, `LimitsJson`, `SettingsJson`, `FileDisplayStore`, `FileLimitStore`, `HostSettings`, `SettingsFile` |
| `CanvasWm.Engine` | `CanvasWm.Engine.Hosting` (new) | `LoopClock`, `NullRenderer`, `NullCapture`, `TickPacingOptions`, `TickSource`, `TickPacer`, `TickCoalescer`, `ShortcutRelay`, `QuitReason`, `LifetimeState`, `AppLifetime`, `HostComposition` |
| `CanvasWm.Win32` | `CanvasWm.Win32.Hosting` (new) | `Win32HostWindow`, `HostNotification`, `DwmComposition`, `HighResolutionTimer` |
| `CanvasWm.Win32` | `CanvasWm.Win32.Displays` (new) | `Win32Displays` |
| `CanvasWm.Win32` | `CanvasWm.Win32.Storage` (new) | `DirectoryTextStore` |
| `CanvasWm.App` | `CanvasWm.App` | `Program`, `app.manifest`, and **internal** types: `HostContext` (composition root, an `ApplicationContext`), `TrayMenu`, `TickDriver`, `UiWake`, `FileLog`, `SingleInstance`, `TrackerWindowMap`, `CrashHooks` |

Tests:
- `tests/CanvasWm.Core.Tests/Displays/DisplayKeyChoiceTests.cs`
- `tests/CanvasWm.Engine.Tests/Persistence/*Tests.cs`,
  `tests/CanvasWm.Engine.Tests/Hosting/*Tests.cs`, new fake
  `tests/CanvasWm.Engine.Tests/Fakes/InMemoryTextFileStore.cs`
- `tests/CanvasWm.Win32.Tests/Hosting/*Tests.cs`, `.../Displays/*Tests.cs`,
  `.../Storage/*Tests.cs`
- `windows-verification/app-host.md` (manual checklist, ACs tagged
  [windows-manual]).

There is **no** App test project (solution-skeleton fixes the project list).
That is why every testable decision lives in Core/Engine (Mac) or Win32
(Windows-automated). The App holds only code whose behaviour is checked by the
manual smoke tests.

Why JSON and stores live in Engine (not App or Win32): the codecs need Engine
types (`SavedDisplay`, `EngineKey`, `WindowKey`, `AppId`) and must be
Mac-testable. They are pure. The only I/O is behind `ITextFileStore`, whose real
implementation (`System.IO`) is in Win32. `System.Text.Json` and
`System.Security.Cryptography` come from the shared framework, so **no package
reference** is added anywhere.

Why clock, pacing, lifetime and relay logic live in Engine.Hosting: they are
orchestration decisions (when to run, when to quit, what a key press does).
They take time and OS effects as delegates, so they are Mac-testable. `Win32`
may not contain `Stopwatch`, timers or `Thread.Sleep` (#10 AC4), and it must
not contain policy.

Depends on: `specs/engine-router.md` (EngineRouter, HotkeyTriggers,
RouterOptions, DisplayInfo, B41–B43), `specs/window-tracker.md`,
`specs/win32-window-enum.md` (Win32WindowSource, `DipSpace`, B17, A8, A13),
`specs/win32-frame-writer.md` (Win32WindowSystem, IManagedWindowMap),
`specs/win32-hotkeys.md` (Win32HotkeySource, HotkeyReport),
`specs/shortcut-model.md` (ShortcutStore, ShortcutProfiles, A4),
`specs/engine-interfaces.md` (IClock B19, ILimitStore B13, IDisplayStore
B16–B18, SavedDisplay B14/B15, IRenderer, ICapture), `specs/layout-tree.md`
(A1 wire shape, A2 decode defaults), `specs/engine-inbound-api.md`
(DisplayEngine constructor, Tick time base B39), `specs/solution-skeleton.md`.

Coordinates: everything that reaches the router or an engine is in DIPs, y-down,
virtual-desktop origin. Win32 converts with `DipSpace` only (CLAUDE.md
mixed-DPI rule; #10 A8).

### Project-file changes

| File | Change |
|---|---|
| `src/CanvasWm.App/CanvasWm.App.csproj` | add `<UseWindowsForms>true</UseWindowsForms>`, `<ApplicationManifest>app.manifest</ApplicationManifest>`, `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`, `<NoWarn>$(NoWarn);WFAC010</NoWarn>` (A14). References unchanged (Core, Engine, Win32). No `PackageReference`. |
| `src/CanvasWm.App/app.manifest` | new (B60) |
| `src/CanvasWm.Core/*.csproj`, `src/CanvasWm.Engine/*.csproj`, `src/CanvasWm.Win32/*.csproj` | unchanged |
| tests csproj | unchanged |

## Public API

A non-nullable reference parameter that is null throws
`ArgumentNullException` before any state change. All public members carry
XML docs (CS1591 is an error).

### Core — display key choice

```csharp
namespace CanvasWm.Core.Displays;

/// <summary>One active display path as the OS reports it: the GDI source name it drives
/// (e.g. "\\.\DISPLAY1") and the monitor's device path. Either may be null or empty when
/// the OS could not report it.</summary>
public readonly record struct DisplayPathName(string? SourceName, string? DevicePath);

/// <summary>Chooses one stable, unique key per monitor (B1). Pure.</summary>
public static class DisplayKeyChoice
{
    /// <summary>One key per entry of monitorSourceNames, in the same order.</summary>
    /// <param name="monitorSourceNames">Each monitor's GDI device name, in enumeration order
    /// (null or empty when unknown).</param>
    /// <param name="paths">Active display paths in OS order.</param>
    public static IReadOnlyList<string> Assign(IReadOnlyList<string?> monitorSourceNames,
                                               IReadOnlyList<DisplayPathName> paths);
}
```

### Engine — persistence

```csharp
namespace CanvasWm.Engine.Persistence;

/// <summary>Outcome of reading a named text file.</summary>
public enum TextFileStatus { Ok = 0, Missing = 1, Failed = 2 }

/// <summary>Read result. Content is non-null exactly when Status is Ok.</summary>
public readonly record struct TextFileRead(TextFileStatus Status, string? Content);

/// <summary>A flat folder of small UTF-8 text files, addressed by plain file name. The only
/// file I/O seam of the stores. Implementations never throw for storage failures; they
/// report them through the return values. Called on the engine thread.</summary>
public interface ITextFileStore
{
    /// <summary>Reads the whole file. Missing when it does not exist; Failed when it exists
    /// but cannot be read.</summary>
    /// <exception cref="ArgumentException">name is not valid (TextFileNames.Validate).</exception>
    TextFileRead Read(string name);

    /// <summary>Atomically replaces (or creates) the file with content: a reader sees either
    /// the old or the new content, never a mix. True on success.</summary>
    /// <exception cref="ArgumentException">name is not valid.</exception>
    bool Write(string name, string content);

    /// <summary>Renames name to newName, replacing newName if it exists. False when name is
    /// missing or the rename failed.</summary>
    /// <exception cref="ArgumentException">a name is not valid.</exception>
    bool Move(string name, string newName);
}

/// <summary>File-name rules shared by every ITextFileStore (B10).</summary>
public static class TextFileNames
{
    /// <summary>Throws when name is not a plain, portable file name.</summary>
    /// <exception cref="ArgumentNullException">name is null.</exception>
    /// <exception cref="ArgumentException">name is invalid (B10).</exception>
    public static void Validate(string name);
}

/// <summary>Names of the data folder and its files (B11).</summary>
public static class DataFiles
{
    public const string DataFolderName = "CanvasWM";
    public const string LayoutsFolderName = "layouts";
    public const string LogsFolderName = "logs";
    public const string LimitsFileName = "limits.json";
    public const string SettingsFileName = "settings.json";
    public const string LogFileName = "canvaswm.log";
    public const string CorruptSuffix = ".corrupt";

    /// <summary>"arrangement-" + lowercase hex SHA-256 of UTF-8(key.Value) + ".json" (B12).</summary>
    public static string LayoutFileName(EngineKey key);
}

/// <summary>JSON codec of one saved display record (B13–B19). Pure.</summary>
public static class SavedDisplayJson
{
    /// <summary>The format version written and accepted: 1.</summary>
    public const int Version = 1;

    /// <summary>Encodes key + display as the file text (B13–B16).</summary>
    /// <exception cref="NotSupportedException">display holds a value that has no wire form
    /// yet: a hidden panel, a hidden desktop with a restore target, or a non-empty dock-spot
    /// map (B17).</exception>
    public static string Encode(EngineKey key, SavedDisplay display);

    /// <summary>Decodes file text (B18, B19). On false, key and display are null and error
    /// is a non-empty one-line reason.</summary>
    public static bool TryDecode(string json,
                                 [NotNullWhen(true)] out EngineKey? key,
                                 [NotNullWhen(true)] out SavedDisplay? display,
                                 out string? error);
}

/// <summary>JSON codec of the learned-minimum map (B20). Pure.</summary>
public static class LimitsJson
{
    public const int Version = 1;
    public static string Encode(IReadOnlyDictionary<AppId, Size> limits);
    public static bool TryDecode(string json,
                                 [NotNullWhen(true)] out IReadOnlyDictionary<AppId, Size>? limits,
                                 out string? error);
}

/// <summary>JSON codec of the flat settings map (B21). Pure.</summary>
public static class SettingsJson
{
    public const int Version = 1;
    public static string Encode(IReadOnlyDictionary<string, string> values);
    public static bool TryDecode(string json,
                                 [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? values,
                                 out string? error);
}

/// <summary>IDisplayStore over an ITextFileStore (B22–B24). Engine-thread only.</summary>
public sealed class FileDisplayStore : IDisplayStore
{
    /// <param name="files">The layouts folder.</param>
    /// <param name="log">One-line diagnostics; may be null.</param>
    public FileDisplayStore(ITextFileStore files, Action<string>? log = null);
    public SavedDisplay? Load(EngineKey key);
    public void Save(EngineKey key, SavedDisplay display);
}

/// <summary>ILimitStore over one file, DataFiles.LimitsFileName (B25, B26).</summary>
public sealed class FileLimitStore : ILimitStore
{
    /// <summary>Reads the file once, now (B25).</summary>
    public FileLimitStore(ITextFileStore files, Action<string>? log = null);
    /// <summary>Snapshot of the entries (copy).</summary>
    public IReadOnlyDictionary<AppId, Size> Entries { get; }
    public Size? Minimum(AppId? app);
    public void Learn(AppId? app, Size minimum);
    public void Observe(AppId? app, Size observed);
}

/// <summary>The settings this build uses (B27).</summary>
/// <param name="Profile">Shortcut profile used as defaults for missing entries.</param>
/// <param name="Shortcuts">Effective bindings.</param>
public sealed record HostSettings(ShortcutProfile Profile, ShortcutBindings Shortcuts)
{
    /// <summary>Key of the profile entry: "shortcuts.profile".</summary>
    public const string ProfileKey = "shortcuts.profile";
    /// <summary>WinAlt profile with its bindings.</summary>
    public static HostSettings Default { get; }
    /// <summary>Reads a flat map (B27).</summary>
    public static HostSettings FromValues(IReadOnlyDictionary<string, string> values);
    /// <summary>ShortcutStore.Format(Shortcuts) plus the profile entry: 16 entries (B27).</summary>
    public IReadOnlyDictionary<string, string> ToValues();
}

/// <summary>Loads settings.json with first-run and corrupt-file handling (B28).</summary>
public static class SettingsFile
{
    public static HostSettings LoadOrCreate(ITextFileStore files, Action<string>? log = null);
}
```

`HostSettings` validates in its init accessors: `Profile` defined
(`ArgumentOutOfRangeException`), `Shortcuts` non-null.

### Engine — hosting

```csharp
namespace CanvasWm.Engine.Hosting;

/// <summary>The real IClock (B30–B35): time from a monotonic source, delayed actions run
/// when the host's wake fires. Engine-thread only (Now may be read from any thread).</summary>
public sealed class LoopClock : IClock
{
    /// <param name="now">Monotonic seconds (the App passes a Stopwatch reading).</param>
    /// <param name="scheduleWake">Asks the host to call RunDue on the engine thread after the
    /// given delay in seconds (finite, &gt;= 0). Each request replaces the previous one. It
    /// must never call RunDue synchronously.</param>
    public LoopClock(Func<double> now, Action<double> scheduleWake);
    public double Now { get; }
    public void After(double seconds, Action action);
    /// <summary>Runs the actions that are due (B33). Called by the host when the wake fires.</summary>
    /// <exception cref="InvalidOperationException">called while After is running (B35).</exception>
    public void RunDue();
    /// <summary>Number of actions not yet run, including never-due ones.</summary>
    public int PendingCount { get; }
    /// <summary>Earliest finite due time, or null.</summary>
    public double? NextDue { get; }
}

/// <summary>IRenderer that draws nothing (until #16).</summary>
public sealed class NullRenderer : IRenderer
{
    public static NullRenderer Instance { get; }
    public void Render(RenderPlan plan);           // null → ANE; otherwise nothing
}

/// <summary>ICapture that captures nothing (until #16): Latest is always null.</summary>
public sealed class NullCapture : ICapture
{
    public static NullCapture Instance { get; }
    public void Start(WindowKey key);
    public void Stop(WindowKey key);
    public void SetRate(WindowKey key, double fps);
    public void Hold(WindowKey key, bool on);
    public CaptureFrame? Latest(WindowKey key);
}

/// <summary>Tick pacing constants (B37). Immutable; every value finite and &gt; 0, the limit
/// &gt;= 1, Min &lt;= Fallback &lt;= Max, else ArgumentOutOfRangeException from the init accessor.</summary>
public sealed record TickPacingOptions
{
    public static TickPacingOptions Default { get; }
    /// <summary>A composition wait shorter than this is suspect, s. Default 0.002.</summary>
    public double FastFlushSeconds { get; init; }
    /// <summary>Consecutive suspect waits that switch to the timer. Default 5.</summary>
    public int FastFlushLimit { get; init; }
    /// <summary>Refresh rate used when none is known, Hz. Default 60.</summary>
    public double FallbackRefreshHz { get; init; }
    /// <summary>Lowest plausible refresh rate, Hz. Default 20.</summary>
    public double MinRefreshHz { get; init; }
    /// <summary>Highest plausible refresh rate, Hz. Default 1000.</summary>
    public double MaxRefreshHz { get; init; }
}

/// <summary>What paces the tick thread.</summary>
public enum TickSource { Composition = 0, Timer = 1 }

/// <summary>Chooses between composition-paced and timer-paced ticks (B37). Single-threaded
/// (the tick thread).</summary>
public sealed class TickPacer
{
    public TickPacer(TickPacingOptions? options = null);
    public TickPacingOptions Options { get; }
    public TickSource Source { get; }
    /// <summary>Consecutive suspect composition waits.</summary>
    public int SuspectCount { get; }
    /// <summary>Back to Composition with a zero count (each frames-on period).</summary>
    public void Reset();
    /// <summary>Records one composition wait.</summary>
    public void ReportFlush(bool succeeded, double elapsedSeconds);
    /// <summary>Timer period for refreshHz (B37).</summary>
    public double TimerPeriodSeconds(double? refreshHz);
}

/// <summary>Allows at most one outstanding tick post (B38). Thread-safe.</summary>
public sealed class TickCoalescer
{
    /// <summary>True when the caller should post a tick now (none outstanding).</summary>
    public bool TryBeginPost();
    /// <summary>The posted tick is being handled; the next signal may post again.</summary>
    public void Delivered();
    public bool IsOutstanding { get; }
}

/// <summary>Connects hotkey triggers, the OS hotkey registration and the router (B39–B41).
/// Engine-thread only.</summary>
public sealed class ShortcutRelay
{
    /// <param name="apply">Registers exactly the given combos and returns one report per distinct
    /// combo (the App passes Win32HotkeySource.Apply).</param>
    public ShortcutRelay(HotkeyTriggers triggers, EngineRouter router,
                         Func<IReadOnlyList<KeyCombo>, IReadOnlyList<HotkeyReport>> apply,
                         Action<string>? log = null);
    /// <summary>Reports of the latest apply; empty before the first.</summary>
    public IReadOnlyList<HotkeyReport> LastReports { get; }
    /// <summary>Raised after every apply with its request and reports.</summary>
    public event Action<HotkeyApplyRequest, IReadOnlyList<HotkeyReport>>? Applied;
    /// <summary>A registered combo was pressed (B41). Returns the route, or null when nothing ran.</summary>
    public CommandRoute? Pressed(KeyCombo combo);
}

/// <summary>Why the app is quitting.</summary>
public enum QuitReason { User = 0, EndSession = 1, Fault = 2 }

/// <summary>Process lifetime state.</summary>
public enum LifetimeState { Running = 0, ShuttingDown = 1, Finished = 2 }

/// <summary>The quit and crash sequence (B42–B47). Engine-thread only, except where noted.</summary>
public sealed class AppLifetime
{
    /// <summary>Extra time after the router's shutdown timeout before the safety net finishes
    /// the quit anyway, s: 0.5.</summary>
    public const double GraceSeconds = 0.5;
    /// <summary>Faults logged in full; later ones are only counted: 20.</summary>
    public const int MaxLoggedFaults = 20;

    /// <param name="stopTracking">Stops OS window events (the App disposes the tracker).</param>
    /// <param name="finish">Called exactly once with the exit code; the App releases OS
    /// resources and leaves its message loop.</param>
    public AppLifetime(EngineRouter router, HotkeyTriggers triggers, IClock clock,
                       Action stopTracking, Action<int> finish, Action<string>? log = null);
    public LifetimeState State { get; }
    /// <summary>Reason of the first quit request, or null.</summary>
    public QuitReason? Reason { get; }
    /// <summary>True once any fault was reported (B46).</summary>
    public bool Faulted { get; }
    /// <summary>Exit code passed to finish: 1 when Faulted, else 0. Meaningful once Finished.</summary>
    public int ExitCode { get; }
    /// <summary>True when Finished, the router's shutdown is complete, or the lifetime's own
    /// deadline passed (B45). Safe to poll in a nested message loop.</summary>
    public bool IsQuitComplete { get; }
    /// <summary>Raised once, right after finish returned (or threw).</summary>
    public event Action? Finished;
    public void RequestQuit(QuitReason reason);
    /// <summary>Logs the error and quits with exit code 1 (B46).</summary>
    public void ReportFault(Exception error);
}

/// <summary>Pure composition helpers.</summary>
public static class HostComposition
{
    /// <summary>The router's engine factory (B48).</summary>
    public static Func<EngineKey, DisplayInfo, DisplayEngine> EngineFactory(
        EnginePolicy policy, IWindowSystem windows, ICapture capture, IClock clock,
        ILimitStore limits, IDisplayStore displays, Func<DisplayInfo, IRenderer> renderers);
}
```

### Win32

```csharp
namespace CanvasWm.Win32.Hosting;

/// <summary>Messages the host window turns into notifications (B52).</summary>
public enum HostNotification
{
    DisplayChanged = 0, SettingChanged = 1, DpiChanged = 2, PowerResumed = 3,
    QueryEndSession = 4, EndSession = 5, CloseRequested = 6, InstanceActivated = 7,
}

/// <summary>Hidden top-level window owned by the engine thread: the post queue for every
/// Win32 component and the receiver of broadcast messages. Mechanism only.</summary>
public sealed unsafe class Win32HostWindow : IDisposable
{
    /// <summary>Window class name: "CanvasWm.HostWindow".</summary>
    public const string WindowClassName = "CanvasWm.HostWindow";
    /// <summary>Registered message name used by a second instance: "CanvasWm.ActivateInstance".</summary>
    public const string ActivateMessageName = "CanvasWm.ActivateInstance";

    /// <summary>Creates the window on the calling thread, which becomes the owner thread and must
    /// run a message loop (B49).</summary>
    /// <param name="onError">Receives exceptions thrown by posted actions or notification
    /// handlers, on the owner thread (B51).</param>
    /// <param name="log">Thread-safe one-line diagnostics; may be null.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">class or window creation failed.</exception>
    public Win32HostWindow(Action<Exception> onError, Action<string>? log = null);

    /// <summary>Schedules action to run later on the owner thread, FIFO. Any thread. Never runs
    /// the action inside this call (B50). After Dispose the action is dropped.</summary>
    public void Post(Action action);
    /// <summary>Actions queued and not yet run.</summary>
    public int PendingCount { get; }
    /// <summary>Raised on the owner thread, synchronously inside the window procedure (B52).</summary>
    public event Action<HostNotification>? Notified;
    /// <summary>Posts the activate message to another running host window (B53). True if posted.</summary>
    public static bool SignalExisting();
    /// <summary>Waits up to waitMilliseconds for input on the calling thread's queue, then
    /// dispatches what is queued (at most 10 000 messages). Returns the number dispatched (B54).</summary>
    public static int PumpPending(int waitMilliseconds);
    /// <summary>Destroys the window; owner thread only, idempotent (B55).</summary>
    /// <exception cref="InvalidOperationException">called on another thread.</exception>
    public void Dispose();
}

/// <summary>DWM composition helpers for the tick thread.</summary>
public static class DwmComposition
{
    /// <summary>DwmFlush: blocks until the next composition pass. True on S_OK.</summary>
    public static bool Flush();
    /// <summary>Composition refresh rate in Hz from DwmGetCompositionTimingInfo(NULL), or null.</summary>
    public static double? RefreshRate();
}

/// <summary>A waitable timer for the tick thread, high resolution when the OS supports it.</summary>
public sealed class HighResolutionTimer : IDisposable
{
    /// <exception cref="System.ComponentModel.Win32Exception">no timer could be created.</exception>
    public HighResolutionTimer();
    public bool IsHighResolution { get; }
    /// <summary>Blocks the calling thread for seconds (relative). True on success.</summary>
    /// <exception cref="ArgumentOutOfRangeException">seconds negative or not finite.</exception>
    /// <exception cref="ObjectDisposedException">after Dispose.</exception>
    public bool Wait(double seconds);
    public void Dispose();
}

namespace CanvasWm.Win32.Displays;

/// <summary>Current displays as DisplayInfo (B56, B57). Never throws for OS failures.</summary>
public static class Win32Displays
{
    public static IReadOnlyList<DisplayInfo> Snapshot();
}

namespace CanvasWm.Win32.Storage;

/// <summary>ITextFileStore over one directory with System.IO (B58).</summary>
public sealed class DirectoryTextStore : ITextFileStore
{
    /// <exception cref="ArgumentException">directory is not a fully qualified path.</exception>
    public DirectoryTextStore(string directory);
    public string Directory { get; }
    public TextFileRead Read(string name);
    public bool Write(string name, string content);
    public bool Move(string name, string newName);
}
```

After this feature the public types of `CanvasWm.Win32` are exactly the 9 from
#10–#12 plus these 6: `Win32HostWindow`, `HostNotification`, `DwmComposition`,
`HighResolutionTimer`, `Win32Displays`, `DirectoryTextStore` (15 in total).

### App (internal; no public API)

| Type | Responsibility |
|---|---|
| `Program` | `[STAThread] static int Main(string[] args)`: process start (B59) |
| `HostContext : ApplicationContext` | composition root (B61–B63), host-notification handling (B64), quit/finish (B66) |
| `TrayMenu` | `NotifyIcon` + `ContextMenuStrip` with two items (B65) |
| `TickDriver` | tick thread over `TickPacer`, `TickCoalescer`, `DwmComposition`, `HighResolutionTimer` (B67) |
| `UiWake` | `System.Windows.Forms.Timer` + host post that serve `LoopClock`'s `scheduleWake` (B34) |
| `FileLog` | thread-safe line log in `logs\canvaswm.log` (B68) |
| `SingleInstance` | named mutex (B59) |
| `TrackerWindowMap : IManagedWindowMap` | adapter over `WindowTracker.IdentityOf/KeyOf` (router B43) |
| `CrashHooks` | `Application.ThreadException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` (B69) |

## Behaviour

### A. Display keys (Core)

- **B1 (`DisplayKeyChoice.Assign`).** For monitor `i` (in order), with source
  name `g = monitorSourceNames[i]`:
  1. the first `p` in `paths` (list order) with non-empty `p.SourceName` equal
     to `g` (`StringComparison.OrdinalIgnoreCase`) and non-empty
     `p.DevicePath` → candidate `p.DevicePath` (unchanged). Cloned outputs
     share a source, so the first path's target wins (findings M4).
  2. otherwise candidate = `g` when non-empty, else `"display-" + i`
     (invariant culture).
  3. If the candidate equals (ordinal) a key already assigned to an earlier
     monitor, append `"#" + n` with the smallest `n >= 2` that makes it unique.
  Null lists → `ArgumentNullException`. Empty monitor list → empty result.

### B. Files and names (Engine.Persistence)

- **B10 (`TextFileNames.Validate`).** Null → ANE. Invalid (→
  `ArgumentException`): empty; `"."` or `".."`; longer than 200 chars; any
  char `< 0x20`; any of `< > : " / \ | ? *`; ending with `.` or space. Every
  other name is valid. Both the in-memory fake and `DirectoryTextStore` call
  it first.
- **B11 (data folder).** The App's data root is
  `Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData),
  DataFiles.DataFolderName)` = `%LOCALAPPDATA%\CanvasWM` (human decision).
  Layout files live in `<root>\layouts`, `limits.json` and `settings.json` in
  `<root>`, the log in `<root>\logs\canvaswm.log`. Folders are created on
  first write.
- **B12 (layout file name).** `"arrangement-" +
  Convert.ToHexStringLower(SHA256.HashData(UTF8(key.Value))) + ".json"`
  (81 chars, only `[a-z0-9.-]`). Engine keys contain `|` and device paths
  contain `\`, `#`, `{` and `?`, which are not usable in file names; the key
  itself is stored inside the file (B13).

### C. SavedDisplay wire format (Engine.Persistence)

- **B13 (top level).** One JSON object, keys in this order:
  `version` (integer 1), `engineKey` (`EngineKey.Value`), `doc` (B14),
  `framed` (string or null), `framings` (object: slot → array of node ids),
  `owners` (object: window key → owner string), `dockSpots` (written only
  when non-null; must then be empty → `{}`), `floatSizes` (written only when
  non-null: window key → size). Map entries are written sorted by key with
  `StringComparer.Ordinal`, so equal records give equal text whatever the
  insertion order.
- **B14 (document and nodes)** — layout-tree A1, nested shapes fixed here:
  - document: `root` (node or null), `floating` (array of floating panels),
    `hidden` (array; always `[]`, B17), `hiddenDesktop` (object or null);
  - split: `kind:"split"`, `id`, `axis` (`Axis.ToRaw()`), `weights` (array of
    numbers), `children` (array of nodes);
  - panel: `kind:"panel"`, `id`, `views` (array of strings), `selected`
    (string or null), `tabbed` (bool);
  - desktop: `kind:"desktop"`, `id`, `child` (node or null);
  - floating panel: `panel` (panel node), `rect` (`{x,y,w,h}`), `z` (integer),
    `size` (`{w,h}` or null);
  - hidden desktop: `group` (string or null), `floats` (array of floating
    panels), `restore` (always null, B17), `neighbourLeaves` (array of
    strings), `weight` (number or null);
  - rect `{ "x", "y", "w", "h" }`, size `{ "w", "h" }`.
- **B15 (numbers).** A finite `double` is written as a JSON number with the
  shortest round-trip form (`Utf8JsonWriter.WriteNumberValue(double)`). NaN,
  +∞ and −∞ (which a raw tree may hold, layout-tree B5) are written as the
  JSON strings `"NaN"`, `"Infinity"`, `"-Infinity"`. Decoding accepts a
  number or exactly those three strings wherever a double is expected.
  Integers (`version`, `z`) are JSON integers in `int` range.
- **B16 (text form).** UTF-8 without BOM; `Utf8JsonWriter` with
  `Indented = true`, `IndentSize = 2`, `NewLine = "\n"`,
  `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`; the text ends with
  one `"\n"`. No `"\r"` anywhere (identical bytes on Mac and Windows). No
  reflection-based `JsonSerializer`: hand-written writer and `JsonDocument`
  reader (trim-safe, exact control of the shape).
- **B17 (values without a wire form).** `RestoreTarget` has no concrete kinds
  yet (layout-tree A11), so `Encode` throws `NotSupportedException` when
  `Document.Hidden` is non-empty, `Document.HiddenDesktop?.Restore` is
  non-null, or `DockSpots` is non-null and non-empty. Nothing in the current
  engine produces such values (they only come from a loaded file, and this
  codec never produces them). The hide/restore feature extends the codec.
- **B18 (decode defaults)** — layout-tree A2. A missing key **or** a JSON
  `null` where an optional value is expected gives the default:
  `framed` → null; `framings`, `owners` → empty; `dockSpots`, `floatSizes` →
  null (absent); `root` → null; `floating`, `hidden` → empty; `hiddenDesktop`
  → null; panel `selected` → null, `tabbed` → false, `views` → empty; split
  `weights`, `children` → empty; desktop `child` → null, desktop `id` →
  `"desktop"`; floating `size` → null; hidden-desktop `group` → null,
  `floats`/`neighbourLeaves` → empty, `restore` → null, `weight` → null.
  Unknown keys are ignored (forward compatibility). For a duplicated
  property name the last occurrence wins.
- **B19 (decode rejects)** — `TryDecode` returns false (whole file) when:
  the text is not valid JSON (comments and trailing commas are invalid;
  `MaxDepth` 256); the top level is not an object; `version` is missing, not
  an integer, or not 1; `engineKey` is missing, not a string, or
  `EngineKey.TryParse` fails; `doc` is missing, null or not an object; a node
  is not an object, has a missing/unknown `kind`, a split or panel without a
  string `id`, an unknown `axis` (a split without `axis` is rejected too);
  a value has the wrong JSON type (e.g. `weights: ["abc"]`, `z: 1.5`,
  `tabbed: "yes"`); an array that must hold nodes/strings contains null; a
  floating panel's `panel` is not a panel node; a `restore` is non-null; a
  `dockSpots` map is non-empty; an `owners`/`floatSizes`/`dockSpots` key is
  empty (not a valid `WindowKey`); a `hidden` array is non-empty. Any
  exception thrown while building the model is caught and becomes a false
  result. Decoding never throws except `ArgumentNullException` for null text.

### D. Other codecs

- **B20 (`LimitsJson`).** `{"version":1,"limits":{"<app id>":{"w":n,"h":n}}}`,
  entries sorted ordinally, same writer settings as B16. Decode rejects (whole
  file): invalid JSON, wrong version, `limits` missing or not an object, an
  empty app id, a value that is not an object with finite numbers `w`, `h`
  `>= 0`. Non-finite strings are **not** accepted here (`LimitRules` never
  stores them).
- **B21 (`SettingsJson`).** `{"version":1,"values":{"<key>":"<string>"}}`,
  sorted ordinally, B16 writer settings. Decode rejects: invalid JSON, wrong
  version, `values` missing or not an object, any value that is not a JSON
  string (null included). Keys are kept as given (any string, ordinal).

### E. Stores

- **B22 (`FileDisplayStore.Load`).** Null key → ANE. `name =
  DataFiles.LayoutFileName(key)`; `r = files.Read(name)`:
  1. `Missing` → null.
  2. `Failed` → log one line, null.
  3. `TryDecode` false → log one line with the reason, `files.Move(name, name +
     CorruptSuffix)` (replacing an older copy), null (engine-interfaces B17).
  4. decoded key ≠ `key` → log one line, null; the file is left untouched.
  5. otherwise the decoded record.
- **B23 (`FileDisplayStore.Save`).** Null arguments → ANE. `text =
  Encode(key, display)`; `NotSupportedException` → log one line, nothing
  written, return. If `text` equals the text this instance last wrote
  successfully under `name` → nothing (no write). Else `files.Write(name,
  text)`: true → remember `text`; false → log one line. Never throws for
  storage failures (engine-interfaces B17).
- **B24.** One `FileDisplayStore` serves every engine. There is no lookup by
  bare display key and no listing (engine-interfaces B18; listing is #24).
- **B25 (`FileLimitStore` construction).** Reads `limits.json` once:
  `Ok` + decode → entries; decode failure → log, move to
  `limits.json.corrupt`, empty; `Missing` or `Failed` → empty (Failed is
  logged). Nothing is written at construction.
- **B26 (`FileLimitStore` calls).** Implements engine-interfaces B13 with
  `LimitRules` (keys by `AppId` equality). A call whose resulting entry
  differs from the previous one (including creation) updates memory, then
  writes the whole map with `LimitsJson.Encode` via `files.Write`; a failed
  write is logged and memory keeps the new value. A call that changes nothing
  writes nothing.

### F. Settings

- **B27 (`HostSettings`).** `FromValues(v)`: `Profile` = the
  `ShortcutProfile` whose name (`WinAlt`, `CtrlAlt`; ordinal) equals
  `v["shortcuts.profile"]`; missing or unknown → `WinAlt`. `Shortcuts =
  ShortcutStore.Parse(v, ShortcutProfiles.Get(Profile))` (shortcut-model A4:
  the profile supplies the defaults). `ToValues()` =
  `ShortcutStore.Format(Shortcuts)` plus `["shortcuts.profile"] =
  Profile.ToString()` (16 entries). `Default = new(WinAlt,
  ShortcutProfiles.WinAlt)`.
- **B28 (`SettingsFile.LoadOrCreate`).** Read `settings.json`:
  - `Ok` and decodes → `HostSettings.FromValues(values)`; the file is **not**
    rewritten (unknown keys, e.g. future #34 keys, stay untouched);
  - `Ok` but does not decode → log, move to `settings.json.corrupt`, write
    `Default.ToValues()`, return `Default`;
  - `Missing` (first run) → write `Default.ToValues()`, return `Default`;
  - `Failed` → log, return `Default`, write nothing.
  Write failures are logged and ignored. Policy values, exclusions and
  start-at-login are not read in this build (#34); the engines use
  `EnginePolicy.Default`, the tracker `ClassifierPolicy.Default`, the router
  `StepTowardPointer = true`.

### G. Clock (Engine.Hosting)

- **B30 (`Now`).** `Now = max(previous Now, now())` — never decreases even if
  the source does (engine-interfaces B19). A non-finite `now()` value is
  ignored (previous value kept; initial previous = −∞, so the first finite
  value is taken; if the very first reading is not finite, `Now` is 0).
- **B31 (`After`).** Null action → ANE. `d = seconds` if finite and `> 0`,
  else 0 for `<= 0` or NaN; `+∞` → stored but never due (no wake requested).
  Store `(due = Now + d, seq = ++counter)`. If `due` is earlier than the
  currently requested wake (or none is requested), request
  `scheduleWake(max(0, due − Now))` and remember `due` as the requested wake.
  The action is never run inside `After`.
- **B32 (wake bookkeeping).** At most one wake is outstanding from the
  clock's point of view; a new request replaces the old one (the host must
  honour "replace").
- **B33 (`RunDue`).** Clear the requested wake. Let `t = Now`, `s =` the seq
  counter at entry. Repeatedly remove and run the pending action with the
  smallest `(due, seq)` such that `due <= t` and `seq <= s`; stop when none is
  left. Actions added while running (even with `due <= t`) wait for the next
  wake, so a self-rescheduling `After(0)` cannot starve the message loop.
  Finally (also when an action throws, in a `finally`): if a finite pending
  due exists, request `scheduleWake(max(0, nextDue − Now))`. An exception
  from an action propagates out of `RunDue` after that re-arm; actions not
  yet run stay pending. A `RunDue` when nothing is due (early wake) runs
  nothing and re-arms.
- **B34 (App wake).** `UiWake` implements `scheduleWake(delay)`: delay `== 0`
  → `host.Post(clock.RunDue)` unless a posted run is already pending; delay
  `> 0` → one `System.Windows.Forms.Timer` with `Interval = clamp(ceiling(delay
  × 1000), 1, int.MaxValue)` ms, restarted (replacing any earlier arming); on
  `Tick` the timer stops and calls `clock.RunDue()`. The clock's source is
  `Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency` relative to process
  start (QPC). Timer granularity is about 16 ms (A2).
- **B35 (never inline).** `RunDue` invoked while `After` is executing (a
  `scheduleWake` that runs `RunDue` synchronously) throws
  `InvalidOperationException`; this makes a host contract violation visible in
  tests.

### H. Ticks (Engine.Hosting + App)

- **B37 (`TickPacer`).** Starts in `Composition`, count 0.
  `ReportFlush(ok, e)`: if `!ok`, or `e` not finite, or `e <
  FastFlushSeconds` → count + 1, else count = 0. When the count reaches
  `FastFlushLimit` the source becomes `Timer` (and stays until `Reset`).
  `ReportFlush` in `Timer` mode does nothing. `Reset()` → `Composition`,
  count 0. `TimerPeriodSeconds(hz)` = `1 / hz` when `hz` is finite and in
  `[MinRefreshHz, MaxRefreshHz]`, else `1 / FallbackRefreshHz`.
- **B38 (`TickCoalescer`).** `TryBeginPost()` atomically changes "not
  outstanding" to "outstanding" and returns true, or returns false when a
  tick is already outstanding. `Delivered()` clears it. Never more than one
  tick message is queued (findings N6).
- **B67 (App `TickDriver`).** One driver for all displays (studied: one per
  display; A10).
  - `SetRunning(true)` (from `router.FramesNeededChanged(true)`): start the
    background thread on first use (`IsBackground = true`, name
    `"CanvasWm.Tick"`, priority `AboveNormal`), `pacer.Reset()`, set the run
    signal. `SetRunning(false)`: clear the run signal; the thread parks after
    its current wait. Both only touch thread-safe signals — no engine call
    (router B29 handler rule).
  - Thread loop while not disposed: wait for the run signal; in
    `Composition` mode measure `DwmComposition.Flush()` with `Stopwatch` and
    `ReportFlush(ok, elapsed)`; in `Timer` mode `timer.Wait(TimerPeriodSeconds(
    DwmComposition.RefreshRate()))` (rate read once per frames-on period;
    timer created lazily; if it cannot be created, `Thread.Sleep` of the
    period rounded up to ms). After each wait, if still running and
    `coalescer.TryBeginPost()` → `host.Post(OnTick)`.
  - `OnTick` (engine thread): `coalescer.Delivered()`; if
    `router.FramesNeeded` → `router.Tick(clock.Now)` (engine-inbound-api B39:
    same time base as `IClock.Now`).
  - Exceptions inside the loop are logged and switch the pacer to `Timer`
    (by reporting failures); the thread never dies from them.
  - On frames-off the driver logs one line: ticks, elapsed seconds, maximum
    interval, source (a frame later than 1.5 × the period is counted as late
    and the count is logged; studied rule 22: late frames are only logged).
  - `Dispose`: stop flag, set the run signal, do not join.

### I. Shortcut relay (Engine.Hosting)

- **B39 (wiring).** The constructor subscribes `router.Started →
  triggers.Start()` and `triggers.ApplyRequested → OnApply`. It must be
  constructed before `tracker.Start()` (B62) so the first `Started` is not
  missed. It calls nothing else.
- **B40 (`OnApply(req)`).** `reports = apply(req.Desired)`;
  `LastReports = reports`; log one summary line (reason, active count, count
  per non-active status) and one line per report whose `IsActive` is false;
  raise `Applied(req, reports)`. Exceptions from `apply` propagate (fault
  path).
- **B41 (`Pressed(combo)`).** `cmd = triggers.Pressed(combo)`; null → return
  null (recording, stopped, not started or unmatched); else return
  `router.Execute(cmd)` (null when no target engine or shutting down; the key
  is still consumed, #7 B3). Exceptions propagate.

### J. Lifetime (Engine.Hosting)

- **B42 (`RequestQuit(r)`).** Only in `Running` (otherwise no-op): `State =
  ShuttingDown`, `Reason = r`, `quitStart = clock.Now`; then, each step in its
  own `try/catch` (an exception is logged and counts as a fault, B46, but the
  sequence continues):
  1. `triggers.Stop()` (→ `Apply([])` through the relay; findings N8 step 1);
  2. `stopTracking()` (WinEvent hooks removed; the tracker keeps its records,
     so `TrackerWindowMap` still resolves keys for release writes, router B41);
  3. `router.BeginShutdown()` (save all, release all, completion counting,
     router B32);
  4. `clock.After(router.Options.ShutdownTimeoutSeconds + GraceSeconds,
     Finish)` — safety net when the router's own completion never fires
     (e.g. step 3 threw before arming its deadline).
- **B43 (completion).** The constructor subscribes `router.ShutdownCompleted
  → clock.After(0, Finish)`. `Finish` is always deferred to a later clock run,
  never run inside the router event (the last completion arrives inside a
  window-system completion; disposing it there would be unsafe). Hence with
  zero engines the router completes inside `BeginShutdown`, and `Finish`
  still runs on the next `RunDue`, not inside `RequestQuit`.
- **B44 (`Finish`).** Idempotent; only from `ShuttingDown`. `State =
  Finished`, `ExitCode = Faulted ? 1 : 0`, log one line (reason, exit code,
  number of faults), call `finish(ExitCode)`; then raise `Finished` (also if
  `finish` threw; that exception is logged and swallowed).
- **B45 (`IsQuitComplete`).** `State == Finished` **or**
  `router.IsShutdownComplete` **or** (`State == ShuttingDown` and `clock.Now >=
  quitStart + ShutdownTimeoutSeconds + GraceSeconds`). Used by the nested
  end-session pump (B64), which therefore cannot wait past the deadline even
  when no timer message is dispatched.
- **B46 (`ReportFault(e)`).** Null → ANE. `Faulted = true` (unless already
  `Finished`); log the full exception text for the first `MaxLoggedFaults`
  faults, then only count them; if `Running` → `RequestQuit(Fault)`. In
  `ShuttingDown` the quit simply continues (exit code becomes 1). After
  `Finished` → log/count only.
- **B47.** `ExitCode`: 0 for a clean `User`/`EndSession` quit, 1 whenever a
  fault was reported before `Finish`. The App adds code 2 for a startup
  failure (B63).

### K. Composition helper (Engine.Hosting)

- **B48 (`EngineFactory`).** Returns `(key, d) => new DisplayEngine(key,
  d.WorkArea, policy, new EngineServices(windows, capture, clock,
  renderers(d), limits, displays))`. Arguments are captured at call time
  (null → ANE). The router sets display frames and wires callbacks (router
  B5). The App passes `NullCapture.Instance` and `_ => NullRenderer.Instance`
  until #16 (findings M7). The current engine does not wait for captured
  pictures before starting (findings M7, Observed); `NullCapture` therefore
  never blocks a start.

### L. Host window (Win32)

- **B49 (creation).** Registers the class `CanvasWm.HostWindow` once per
  process (`ERROR_CLASS_ALREADY_EXISTS` tolerated) and creates a **top-level**
  window (parent 0, not `HWND_MESSAGE`; findings K4) with style `WS_POPUP`,
  extended style `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, position (0,0), size
  0×0, title `"CanvasWM"`, never shown (no `WS_VISIBLE`, no `ShowWindow`). It
  therefore receives broadcasts but never appears in Alt+Tab or the taskbar.
  The activate message id = `RegisterWindowMessageW(ActivateMessageName)`;
  `ChangeWindowMessageFilterEx(hwnd, id, MSGFLT_ALLOW)` so a second instance
  at a different integrity level can still signal (failure ignored). The
  owner thread is the constructing thread. Own windows are already excluded
  from tracking by process id (#10/#30).
- **B50 (`Post`, chosen over `Control.BeginInvoke`; A1).** `Post(a)`:
  `a` null → ANE; after `Dispose` → dropped (counted, logged once); else
  enqueue in a `ConcurrentQueue<Action>`, then if `Interlocked.Exchange(ref
  wakePending, 1) == 0` → `PostMessageW(hwnd, WM_APP + 1, 0, 0)`; if that
  fails, `wakePending = 0` and log one line (the next `Post` retries). `Post`
  never invokes any action (#10 B17 / #10 A13 "never inline": the
  `EventPump` throws if its drain runs inside `Enqueue`).
  **Drain** (window procedure, `WM_APP + 1`): `wakePending = 0`; let `n` =
  queue count at entry; dequeue and run up to `n` actions in FIFO order; if
  the queue is still non-empty afterwards, re-arm the wake as in `Post`. A
  drain therefore yields to the message loop between batches (input, timers,
  hotkeys and notifications keep flowing even under a self-reposting action).
  Nested drains (an action that pumps messages) are allowed; each takes the
  next action from the shared queue, so global FIFO order holds.
- **B51 (errors).** The window procedure is `[UnmanagedCallersOnly]`; no
  exception may cross it. An exception from a drained action stops that
  drain (remaining actions stay queued and the wake is re-armed) and is passed
  to `onError`. An exception from a `Notified` handler is passed to `onError`.
  An exception thrown by `onError` itself is logged and swallowed.
- **B52 (notifications)** — raised synchronously on the owner thread:

  | Message | Notification | Return value |
  |---|---|---|
  | `WM_DISPLAYCHANGE` | `DisplayChanged` | 0 |
  | `WM_SETTINGCHANGE` (any wParam; includes `SPI_SETWORKAREA` 0x2F) | `SettingChanged` | 0 |
  | `WM_DPICHANGED` | `DpiChanged` (window not resized) | 0 |
  | `WM_POWERBROADCAST` with `PBT_APMRESUMEAUTOMATIC` (0x12) | `PowerResumed` | TRUE |
  | `WM_POWERBROADCAST`, other wParam | none | TRUE |
  | `WM_QUERYENDSESSION` | `QueryEndSession` | TRUE (never blocks shutdown) |
  | `WM_ENDSESSION` wParam TRUE | `EndSession` | 0 |
  | `WM_ENDSESSION` wParam FALSE | none | 0 |
  | `WM_CLOSE` | `CloseRequested` (window **not** destroyed) | 0 |
  | registered activate message | `InstanceActivated` | 0 |
  | `WM_APP + 1` | drain (B50) | 0 |
  | anything else | none | `DefWindowProcW` |
- **B53 (`SignalExisting`).** `FindWindowW(WindowClassName, null)`; none →
  false. Else `PostMessageW(found, activateId, 0, 0)` → its result. No
  `SendMessage` (#10 AC5).
- **B54 (`PumpPending(ms)`).** `ms < 0` → AOORE.
  `MsgWaitForMultipleObjectsEx(0, null, ms, QS_ALLINPUT, MWMO_INPUTAVAILABLE)`,
  then `PeekMessageW(PM_REMOVE)` / `TranslateMessage` / `DispatchMessageW`
  until the queue is empty or 10 000 messages were dispatched (a
  self-reposting action cannot trap the caller); returns the count. A
  `WM_QUIT` that is peeked is re-posted with `PostQuitMessage` and stops the
  loop (the outer loop must still see it).
- **B55 (`Dispose`).** Owner thread only (else `InvalidOperationException`);
  idempotent; `DestroyWindow`; later `Post`s are dropped; queued actions are
  dropped and their number logged.

### M. Displays (Win32)

- **B56 (`Snapshot`).** Inside the per-monitor-v2 thread DPI scope used by #10
  (`DpiScope`):
  1. `EnumDisplayMonitors`; per monitor `GetMonitorInfoW` with
     `MONITORINFOEXW` → `rcMonitor`, `rcWork`, `dwFlags`, `szDevice`; DPI =
     `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` (fallback 96, as `Monitors`).
     Monitors whose info cannot be read are skipped.
  2. Paths: `GetDisplayConfigBufferSizes` + `QueryDisplayConfig(
     QDC_ONLY_ACTIVE_PATHS)` (retry up to 3 times on
     `ERROR_INSUFFICIENT_BUFFER`); per path, in order,
     `DisplayConfigGetDeviceInfo` `GET_SOURCE_NAME` → `viewGdiDeviceName` and
     `GET_TARGET_NAME` → `monitorDevicePath` (each failure → null) → a
     `DisplayPathName`. Total failure → empty path list (keys fall back to
     `szDevice`).
  3. Keys = `DisplayKeyChoice.Assign(szDevices, paths)` (human decision:
     DISPLAYCONFIG device path, stable per physical monitor and port).
  4. Per monitor: `new DisplayInfo(new DisplayKey(key), DipSpace.ToDip(
     bounds, bounds, dpi), DipSpace.ToDip(work, bounds, dpi), (int)dpi,
     (dwFlags & MONITORINFOF_PRIMARY) != 0)`, in enumeration order.
  Any OS failure yields fewer entries, never an exception; with no monitor
  the result is empty (the router accepts it).
- **B57.** The existing internal `Monitors` helper may be extended (work
  area, device name) and shared; `Win32WindowSystem`'s per-call monitor reads
  are unchanged. No layout or policy decision is made here.

### N. Directory store (Win32)

- **B58 (`DirectoryTextStore`).** `directory` must satisfy
  `Path.IsPathFullyQualified` (else `ArgumentException`); it need not exist.
  Every method validates names first (B10).
  - `Read`: file absent (or directory absent) → `Missing`; read with
    `File.ReadAllText` (UTF-8, BOM tolerated) → `Ok`; `IOException`,
    `UnauthorizedAccessException` → `Failed`.
  - `Write`: `Directory.CreateDirectory`; write UTF-8 **without BOM** to
    `name + "." + Guid.NewGuid().ToString("N") + ".tmp"` (`FileMode.CreateNew`,
    `FileShare.None`), `Flush(flushToDisk: true)`, close, then
    `File.Move(tmp, path, overwrite: true)` (an atomic replace on NTFS). Any
    failure: delete the temp file (best effort), return false. Never throws
    for I/O.
  - `Move`: `File.Move(src, dst, overwrite: true)`; missing source or I/O
    failure → false.
  - Stray `*.tmp` files from an interrupted write are never read (names never
    match) and are left alone.

### O. App process (WinForms)

- **B59 (`Main`).** `[STAThread]`. In order:
  1. Single instance: `Mutex(initiallyOwned: false, name: "Local\\CanvasWM-" +
     WindowsIdentity.GetCurrent().User!.Value)`; `WaitOne(0)` (an
     `AbandonedMutexException` means a crashed earlier instance — treat as
     acquired). Not acquired → `Win32HostWindow.SignalExisting()`, return 0
     at once (no log, no window, no tray; human decision: second launch
     signals the first and exits).
  2. `ApplicationConfiguration.Initialize()` (visual styles, PerMonitorV2 from
     the csproj), `Application.SetUnhandledExceptionMode(
     UnhandledExceptionMode.CatchException)`, install `CrashHooks` (B69).
  3. Open `FileLog` (B68); log a start line (version, pid, command line).
  4. Construct `HostContext` (B61–B63); `Application.Run(context)`.
  5. After `Run` returns: dispose the context (host window, wake timer),
     log the exit code, release and dispose the mutex, return the code.
  Command-line switch `--diagnostic-fault=ui|background` (only for the
  manual crash test, AC40): 5 s after start, post an action that throws
  (`ui`) or start a thread that throws (`background`). Unknown arguments are
  logged and ignored. There is no other argument.
- **B60 (`app.manifest`).** `requestedExecutionLevel level="asInvoker"
  uiAccess="false"` (findings N4: not elevated; elevated windows stay
  unmanaged, #10); `supportedOS` Windows 10/11 GUID
  `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`; `windowsSettings`: `dpiAware` =
  `true/pm` and `dpiAwareness` = `PerMonitorV2` (findings N3).
- **B61 (composition order)** — findings N1, all on the UI thread inside the
  `HostContext` constructor:
  1. `host = new Win32HostWindow(onError: lifetimeOrStartupFault, log)`;
     `post = host.Post`.
  2. `clock = new LoopClock(stopwatchSeconds, uiWake.Schedule)`.
  3. Stores: `layouts = new DirectoryTextStore(root\layouts)`, `rootFiles =
     new DirectoryTextStore(root)`; `displayStore = new FileDisplayStore(
     layouts, log)`; `limitStore = new FileLimitStore(rootFiles, log)`;
     `settings = SettingsFile.LoadOrCreate(rootFiles, log)`.
  4. `map = new TrackerWindowMap()`; `windowSystem = new Win32WindowSystem(
     post, map, log)`; `source = new Win32WindowSource(post)`.
  5. `router = new EngineRouter(Win32Displays.Snapshot(),
     HostComposition.EngineFactory(EnginePolicy.Default, windowSystem,
     NullCapture.Instance, clock, limitStore, displayStore, _ =>
     NullRenderer.Instance), clock, windowSystem)`.
  6. `tracker = new WindowTracker(source, windowSystem, clock, router,
     ClassifierPolicy.Default)`; `map.Tracker = tracker`.
  7. `hotkeys = new Win32HotkeySource(post)`; `triggers = new
     HotkeyTriggers(settings.Shortcuts)`; `relay = new ShortcutRelay(triggers,
     router, hotkeys.Apply, log)`; `hotkeys.Pressed += c => relay.Pressed(c)`.
  8. `lifetime = new AppLifetime(router, triggers, clock, stopTracking:
     tracker.Dispose, finish: Finish, log)`.
  9. `tickDriver`; `router.FramesNeededChanged += tickDriver.SetRunning`;
     `router.Log += log`; `router.EngineRetired += e => log(...)` (nothing to
     dispose: renderers are null until #16).
  10. `tray = new TrayMenu(...)` (B65); `host.Notified += OnNotified` (B64);
      `relay.Applied += OnApplied` (B65).
  11. `tracker.Start()` → `Ready` → router shows desktops (engines start and
      load saved layouts) → `Started` → `triggers.Start()` → hotkeys applied
      (findings N1 steps 9–10; studied rule 5 order).
  There is no permission gate and no wait for an older copy (Windows has no
  accessibility consent; the single-instance mutex replaces studied rules
  2–3).
- **B62.** `relay` (step 7) exists before `tracker.Start()` (step 11), so
  `Started` reaches `triggers.Start()` (B39).
- **B63 (startup failure).** An exception in steps 1–10 → log it, dispose
  whatever was created in reverse order, show one `MessageBox` (own wording:
  "CanvasWM could not start. Details are in the log:" + log path), exit code
  2, without entering `Application.Run`. An exception from step 11 (or later)
  happens after engines may have moved windows → `lifetime.ReportFault(e)` and
  `Application.Run` proceeds, which runs the quit sequence (exit code 1).
- **B64 (`OnNotified`).**
  - `DisplayChanged`, `SettingChanged`, `DpiChanged`: if no snapshot is
    pending, mark one pending and `post(() => { pending = false;
    router.DisplaysChanged(Win32Displays.Snapshot()); })` — one snapshot per
    burst; the router debounces and decides changed vs work-area-only (router
    B14–B18, A3).
  - `PowerResumed`: the same snapshot post, then `tracker.Rescan()` (studied
    rule 6c adapted; no capture to restart yet).
  - `QueryEndSession`: `lifetime.RequestQuit(EndSession)` (returns at once;
    the host answers TRUE).
  - `EndSession`: `lifetime.RequestQuit(EndSession)` (no-op if already
    quitting), then `while (!lifetime.IsQuitComplete)
    Win32HostWindow.PumpPending(20)` — Windows may end the process as soon as
    this message returns, so the release writes are pumped here (≤ 0.02 s
    slices, studied rule 27c; bounded by B45).
  - `CloseRequested` (e.g. `taskkill` without `/F`): `RequestQuit(User)`
    (the Windows counterpart of the studied SIGTERM handling, rule 4).
  - `InstanceActivated`: **placeholder until #34** (which opens settings):
    log one line and show a tray balloon "CanvasWM is already running." (our
    wording). No other effect.
- **B65 (tray).** `NotifyIcon` with `SystemIcons.Application` (no artwork
  copied; A19), tooltip text `"CanvasWM"`, visible from construction. Context
  menu with exactly two items, in this order: **"Retry shortcuts"** →
  `triggers.Retry()` (raises only when started and not stopped, router B36);
  **"Quit CanvasWM"** → `lifetime.RequestQuit(User)`. Both items are disabled
  once the lifetime leaves `Running`. Left click shows the same menu.
  `OnApplied(req, reports)`: when `req.Reason` is `Start` or `Retry` and some
  report is not active → balloon "Some shortcuts could not be registered
  (n). Close the program using them, then choose Retry shortcuts." (our
  wording). Explorer restarts are handled by `NotifyIcon` itself (it re-adds
  the icon on `TaskbarCreated`).
- **B66 (`Finish(code)` in the App).** In order, each step guarded
  (exceptions logged): `tickDriver.Dispose()`; `hotkeys.Dispose()`;
  `source.Dispose()`; `windowSystem.Dispose()`; `tray` hidden and disposed (no
  ghost icon); `ExitCode = code`; `ExitThread()` (ends `Application.Run`). The
  host window and `UiWake` are disposed after `Run` returns (B59.5).
  Quitting via the tray, `WM_CLOSE` or a fault does **not** use a nested
  pump: the normal message loop keeps running until `Finish`. Only
  `EndSession` pumps nested (B64).
- **B68 (`FileLog`).** Appends UTF-8 lines `"<UTC ISO-8601 with ms> <text>"`
  to `<root>\logs\canvaswm.log`, flushed per line (a crash keeps the tail);
  thread-safe (one lock); when the file exceeds 1 MiB it is renamed to
  `canvaswm.1.log` (replacing it) before the next line. All I/O errors are
  swallowed; if the folder cannot be created, logging is disabled. Sources:
  router `Log` (includes engine `Log`, router B5), `Win32WindowSystem` log,
  host log, relay, store logs, lifetime, tick driver, crash hooks,
  `EngineRetired`.
- **B69 (`CrashHooks`).**
  - `Application.ThreadException` (UI-thread exceptions in WinForms
    dispatch: tray clicks, `UiWake` timer → `LoopClock` actions → engine and
    tracker code) and the host's `onError` (drained posts: window events,
    write completions, hotkey presses, ticks, snapshots) →
    `lifetime.ReportFault(e)`; the loop continues and runs the quit (B42).
  - `AppDomain.UnhandledException`: log it (with `IsTerminating`). If raised
    on a thread other than the UI thread and the lifetime is not `Finished`:
    `host.Post(() => lifetime.ReportFault(e))`, then block the faulting thread
    on a wait handle set by `lifetime.Finished`, for at most
    `router.Options.ShutdownTimeoutSeconds + AppLifetime.GraceSeconds + 1.0`
    s, so the UI thread can release the windows before the runtime ends the
    process. On the UI thread: log only.
  - `TaskScheduler.UnobservedTaskException`: log, `SetObserved()`.
  - A hard kill (`taskkill /F`, power loss, debugger stop) runs none of this:
    **parked windows stay off-screen** until #14 (human decision: ship
    without waiting for #14, document the risk). Partial mitigation already
    exists: on the next start the tracker admits windows < 5 % visible and an
    engine re-adopts them from its saved layout.

### Worked examples

1. **First run.** No `%LOCALAPPDATA%\CanvasWM`. Launch → mutex acquired →
   `settings.json` written with 16 entries (WinAlt) → displays: A (1920×1080
   @96, primary), B (2560×1440 @144 at x=1920) → `DisplayInfo` B `Frame =
   (1920, 0, 1706.67, 960)`, keys = their `\\?\DISPLAY#…` paths →
   `tracker.Start()` → engines `<pathA>|default`, `<pathB>|default` start (no
   layout files yet) → `Started` → `Apply(12 action combos)` → tray icon.
   The first engine save creates `layouts\arrangement-<64 hex>.json`.
2. **Unplug B.** `WM_DISPLAYCHANGE` (several) → one snapshot post per burst
   → `router.DisplaysChanged([A])` → torn down, rebuild 1.5 s after the last
   notification, B's windows released onto A's work area, re-show 0.8 s later.
3. **Tray Quit** with two parked windows: `RequestQuit(User)` → `Apply([])`,
   hooks removed, both engines saved and released (writes on the writer
   thread, completions posted) → last completion → router
   `ShutdownCompleted` → `After(0, Finish)` → next wake: sources and window
   system disposed, tray icon removed, loop ends, exit code 0. A hung window
   that never completes → router deadline at +2.0 s → same finish.
4. **Sign-out.** `WM_QUERYENDSESSION` → quit starts; `WM_ENDSESSION(TRUE)` →
   nested pump in 20 ms slices until `IsQuitComplete` (≤ 2.5 s) → return.
5. **Second launch.** Mutex held → `SignalExisting()` → exit 0 in well under
   a second; the first instance logs and shows "CanvasWM is already running."
6. **Corrupt layout.** `arrangement-….json` holds `{"version":1` → engine
   `Start` → `Load` → decode fails → file renamed to `….json.corrupt`, engine
   starts with no saved layout, the next save writes a fresh file.
7. **Fault in a write completion.** Host `onError` → `ReportFault` → quit
   sequence as in example 3 → exit code 1, the log holds the exception.

## Edge cases & errors

- `Win32WindowSource.Subscribe` throwing `Win32Exception` (hook install
  failure, #10 A16) happens inside `tracker.Start()` → B63 second case (fault
  → quit with zero or few engines → exit 1); the log shows the error.
- `Win32HotkeySource.Apply` throws only on contract errors (invalid combo);
  `HotkeyPlan.Desired` never yields such combos. Taken/failed combos are
  reports, not exceptions (B40, B65).
- A drained action throwing repeatedly after the quit started: each is a
  fault (counted), the quit proceeds, the deadline bounds it (B42.4, B45).
- `router.DisplaysChanged` throwing `ArgumentException` for duplicate keys
  cannot happen (B1 makes keys unique); if it did, it is a fault.
- An empty display snapshot (all monitors off, remote session transitions):
  the router accepts it (no engines; windows keep their places) and rebuilds
  on the next snapshot.
- `FileDisplayStore` with a layout file of another engine key (renamed or
  copied by hand): ignored, not overwritten until that engine saves.
- `settings.json` that is valid JSON but holds malformed shortcut strings:
  `ShortcutStore.Parse` treats them as unbound (shortcut-model A11); the file
  is not rewritten.
- `LoopClock` actions that throw: the exception reaches the WinForms
  `Timer.Tick` dispatch → `Application.ThreadException` → fault; remaining
  due actions run at the next wake (B33).
- `Post` from the writer thread after `Win32WindowSystem.Dispose`: the host
  still accepts it until the host itself is disposed after `Run`; actions
  posted after that are dropped (B50, B55).
- `WM_ENDSESSION(FALSE)` (shutdown cancelled) after a quit started: the quit
  is not undone; the app exits (A11).
- A second instance started while the first is still composing (no host
  window yet): `SignalExisting` returns false; the second still exits 0.
- Clock source going backwards or returning NaN: `Now` holds (B30).
- `DirectoryTextStore` on a read-only or full disk: writes return false, the
  stores log and keep working in memory.

## Acceptance criteria

Rig for Engine ACs: `ManualClock`, `FakeWindowSystem`, `FakeCapture`,
`InMemoryLimitStore`/`InMemoryDisplayStore` where a real store is not under
test, real `DisplayEngine`s through `HostComposition.EngineFactory`, real
`EngineRouter`/`HotkeyTriggers`, and the new
`tests/CanvasWm.Engine.Tests/Fakes/InMemoryTextFileStore` (dictionary of
name → text; records `Reads`, `Writes`, `Moves` in order; per-name failure
injection for read (`Failed`), write (false) and move (false); validates names
with `TextFileNames.Validate`). Times compare within 1e-9; geometry exactly.

### [mac] Core / Engine

- **AC1** [mac] `DisplayKeyChoice.Assign`:
  - monitors `["\\.\DISPLAY1","\\.\DISPLAY2"]`, paths `[("\\.\DISPLAY2","P2"),
    ("\\.\display1","P1")]` → `["P1","P2"]` (case-insensitive source match);
  - clone: monitors `["\\.\DISPLAY1"]`, paths `[("\\.\DISPLAY1","PA"),
    ("\\.\DISPLAY1","PB")]` → `["PA"]`;
  - a path with empty device path is skipped → fallback to the next matching
    path, else to the source name;
  - no paths → `["\\.\DISPLAY1","\\.\DISPLAY2"]`; source names `[null, ""]`
    and no paths → `["display-0","display-1"]`;
  - two monitors resolving to `"P"` → `["P","P#2"]`; three → `["P","P#2","P#3"]`;
    monitors resolving to `"P"`, `"P#2"` and `"P"` → `["P","P#2","P#3"]`;
  - empty input → empty; null lists → ANE. (B1)
- **AC2** [mac] Names: `TextFileNames.Validate` accepts `"limits.json"`,
  `"arrangement-ab.json"`, `"a b.json.corrupt"`; rejects null (ANE), `""`,
  `"."`, `".."`, `"a/b"`, `"a\\b"`, `"a:b"`, `"a|b"`, `"a?"`, `"a*"`,
  `"a<"`, `"a>"`, `"a\""`, `"a\u0001"`, `"name."`, `"name "`, a 201-char name
  (`ArgumentException`). `DataFiles.LayoutFileName(EngineKey("A","default"))`
  equals `"arrangement-" + Convert.ToHexStringLower(SHA256.HashData(
  Encoding.UTF8.GetBytes("A|default"))) + ".json"`, has length 81, passes
  `Validate`, and differs from the name for desktop `"d2"`; a key whose
  display is `"\\?\DISPLAY#GSM5B7F#5&1a2b&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"`
  also yields a valid 81-char name. Constants equal B11 values. (B10–B12)
- **AC3** [mac] `SavedDisplayJson` round-trip: a record with a desktop root
  holding a split (axis Y, weights `[1, 2.5]`) of a single-window panel and a
  tabbed two-view panel with `selected` null; an empty desktop as a split
  child; one floating panel with `Size` and one without; a hidden desktop
  with group, two floats, null restore, neighbour leaves and weight 0.3;
  `framed` set; two framings; two owners; `dockSpots` `{}` (non-null empty);
  `floatSizes` with one entry → `TryDecode(Encode(k, r))` gives `k` and a
  record equal to `r` (`SavedDisplay` equality). Also equal for: `dockSpots`
  and `floatSizes` null (and the keys are absent from the text); weights
  `[NaN, +∞, −∞]` and a floating rect with `−∞` X; an engine key whose
  display part contains `\`, `#`, `{`, `|`, `"` and non-ASCII text, and window
  keys with non-ASCII text. (B13–B16, B18)
- **AC4** [mac] Wire shape: for the record below, `JsonNode.DeepEquals(
  JsonNode.Parse(Encode(k, r)), JsonNode.Parse(expected))` is true, where
  `k = "A|default"` and `expected` is:

  ```json
  {
    "version": 1,
    "engineKey": "A|default",
    "doc": {
      "root": {
        "kind": "desktop", "id": "desktop",
        "child": {
          "kind": "split", "id": "s1", "axis": "x", "weights": [1, 2],
          "children": [
            { "kind": "panel", "id": "w1", "views": ["w1"], "selected": "w1", "tabbed": false },
            { "kind": "panel", "id": "p2", "views": ["w2", "w3"], "selected": "w3", "tabbed": true }
          ]
        }
      },
      "floating": [
        { "panel": { "kind": "panel", "id": "w4", "views": ["w4"], "selected": "w4", "tabbed": false },
          "rect": { "x": 10, "y": 20, "w": 300, "h": 200 }, "z": 1, "size": { "w": 300, "h": 200 } }
      ],
      "hidden": [],
      "hiddenDesktop": null
    },
    "framed": "p2",
    "framings": { "1": ["w1"], "2": ["p2", "w1"] },
    "owners": { "w1": "A|default" },
    "floatSizes": { "w4": { "w": 300, "h": 200 } }
  }
  ```

  Additionally: the top-level property names appear in exactly the order
  `version, engineKey, doc, framed, framings, owners, floatSizes`; the text
  contains no `'\r'`, does not start with a BOM, ends with exactly one
  `'\n'`; two records equal except for dictionary insertion order encode to
  identical strings; a NaN weight is written as the string `"NaN"`.
  (B13–B16)
- **AC5** [mac] Decode defaults: `{"version":1,"engineKey":"A|default","doc":{}}`
  → `Document == LayoutDocument.Empty`, `FramedNodeId` null, `Framings` and
  `Owners` empty, `DockSpots` and `FloatSizes` null. A panel without
  `selected`/`tabbed`/`views` → null/false/empty; `"selected": null` same; a
  desktop without `id` → `"desktop"`; a split with axis and id but without
  weights/children → empty arrays; `"floating": null` → empty; an unknown
  extra property at any level is ignored; `{"owners":{"w1":"a","w1":"b"}}` →
  owner `"b"`. (B18)
- **AC6** [mac] Decode rejects (false, non-null non-empty `error`, null
  outputs) each of: `""`, `"nope"`, `"[]"`, a valid file followed by a
  trailing `x`, a comment, a trailing comma; version missing, `2`, `"1"`;
  `engineKey` missing, `"nopipe"`, `"A|"`, `5`; `doc` missing, `null`, `[]`;
  a node with kind `"tree"`, without kind, a split with axis `"z"` or no
  axis, a split or panel without id, `children: [null]`, `weights: ["abc"]`,
  `weights: ["nan"]`, `z: 1.5`, `tabbed: "yes"`, a floating panel whose
  `panel` is a desktop node, `restore: {}`, `hidden: [{}]`, `dockSpots:
  {"w1": {}}`, `owners: {"": "x"}`, nesting deeper than 256.
  `TryDecode(null!)` → ANE. No call throws anything else. (B19)
- **AC7** [mac] `Encode` throws `NotSupportedException` for a document with a
  `HiddenPanel` (test-only `RestoreTarget` subclass), a hidden desktop with a
  non-null restore, and a non-empty `DockSpots`. `FileDisplayStore.Save` of
  each → no exception, no `Write`, exactly one log line. (B17, B23)
- **AC8** [mac] `FileDisplayStore` over `InMemoryTextFileStore`:
  - `Save(k, r)` → one `Write` to `LayoutFileName(k)`; `Load(k)` → equal to
    `r`; a second `Save(k, r)` → no new write; `Save(k, r2)` → one write;
  - `Load` of a never-saved key → null, no write/move, no log;
  - corrupt text under the name → `Load` null, exactly one `Move(name, name +
    ".corrupt")`, one log line; a following `Load` → null (missing), no move;
  - a valid file whose `engineKey` is `"B|default"` stored under A's name →
    `Load(A)` null, no move;
  - injected read failure → null, no move, one log line; injected write
    failure → `Save` returns normally, one log line, and the next `Save` of
    the same record writes again (failed text not remembered);
  - null key/record → ANE. (B22–B24)
- **AC9** [mac] `FileLimitStore`: fresh (missing file) → `Entries` empty, no
  write at construction. Sequence of engine-interfaces AC8 for app
  `"com.x"`: results identical to `InMemoryLimitStore`; `Writes` count equals
  the number of calls that changed the entry (learn 500×300, learn 400×350,
  observe 450×360 → 3; observe 499×349 → none); after each write
  `LimitsJson.TryDecode(lastWrite)` equals `Entries`. Construct over a file
  holding the last written text → same `Entries`. Corrupt file → empty, one
  move to `limits.json.corrupt`. Injected write failure → `Minimum` still
  returns the new value, one log line. `Learn(null, …)`, `Observe(null, …)`,
  `Learn(app, NaN×1)` → no write. `LimitsJson` rejects `{"version":1,
  "limits":{"a":{"w":-1,"h":2}}}`, a `"w":"NaN"` value, and an empty app id.
  (B20, B25, B26)
- **AC10** [mac] Settings:
  - `HostSettings.Default` = (WinAlt, `ShortcutProfiles.WinAlt`);
    `Default.ToValues()` has 16 entries incl. `"shortcuts.profile":"WinAlt"`
    and `ShortcutStore.VersionKey`; `FromValues(ToValues())` equals the
    original for both profiles and for bindings with one action unbound;
  - `FromValues({"shortcuts.profile":"CtrlAlt"})` → Profile CtrlAlt, bindings
    equal `ShortcutProfiles.CtrlAlt`; `"Foo"` or `"ctrlalt"` → WinAlt;
  - `LoadOrCreate`: missing → returns Default, exactly one write of
    `settings.json` whose decoded values equal `Default.ToValues()`; existing
    valid file with CtrlAlt + the Right action's key set to `"none"` + an
    unknown key `"x.y":"z"` → CtrlAlt bindings with Right unbound, **no**
    write; corrupt → Default, one move to `settings.json.corrupt`, one write
    of defaults; read failure → Default, no write, no move;
  - `SettingsJson` rejects a non-string value (`1`, `null`, `{}`) and a wrong
    version; round-trips keys with non-ASCII text. (B21, B27, B28)
- **AC11** [mac] `LoopClock` with a settable `now` source and a recording
  `scheduleWake`:
  - at now 0: `After(1, a)` → wakes `[1]`; `After(0.5, b)` → wakes `[1, 0.5]`;
    `After(2, c)` → no new wake; nothing ran;
  - now 0.4, `RunDue()` → nothing runs, wake `0.1` requested (early wake);
  - now 0.5, `RunDue()` → b only, wake `0.5` requested; now 2.0, `RunDue()` →
    a then c, no wake (nothing pending), `PendingCount` 0;
  - same due → registration order; `After(-1, x)` and `After(NaN, y)` →
    wake 0 requested and run at the next `RunDue` at the same now;
    `After(+∞, z)` → no wake, `PendingCount` stays 1, `NextDue` null;
  - an action that calls `After(0, w)` during `RunDue` → w does not run in
    that `RunDue`; a wake 0 is requested; the next `RunDue` runs it;
  - an action that throws → the exception propagates from `RunDue`, later
    due actions are still pending, a wake was requested;
  - a `scheduleWake` that calls `RunDue` synchronously → `After` throws
    `InvalidOperationException`;
  - `now` source 5, then 4, then NaN → `Now` reads 5, 5, 5; first reading NaN
    → 0; `After(1, null!)` → ANE. (B30–B35)
- **AC12** [mac] Ticks: `TickPacingOptions.Default` = 0.002, 5, 60, 20, 1000;
  invalid values (0, −1, NaN, ∞ for doubles; 0 for the limit; Min >
  Fallback) → AOORE. `TickPacer`: four suspect reports (`(true, 0.001)`,
  `(false, 0.016)`, `(true, NaN)`, `(true, 0.0019)`) → still Composition,
  count 4; `(true, 0.016)` → count 0; five suspect in a row → Timer; further
  reports → Timer; `Reset()` → Composition, 0. `TimerPeriodSeconds(144)` →
  1/144; `(null)`, `(0)`, `(10)`, `(5000)`, `(NaN)` → 1/60; `(20)` → 1/20.
  `TickCoalescer`: `TryBeginPost` true, then false until `Delivered`, then
  true; 8 threads × 10 000 concurrent `TryBeginPost` with no `Delivered` →
  exactly one true. (B37, B38)
- **AC13** [mac] `NullRenderer.Instance.Render(RenderPlan.Empty)` does
  nothing; `Render(null!)` → ANE. `NullCapture`: `Start/Stop/SetRate/Hold`
  with any key do not throw; `Latest` always null (also after `Start`); null
  key → ANE. Both `Instance`s are the same object on every read. (B48)
- **AC14** [mac] `HostComposition.EngineFactory(policy, win, cap, clock,
  limits, store, d => renderers[d.Key])`: `f(k, d)` → engine with `Key == k`,
  `VisibleArea == d.WorkArea`, `Policy` equal to `policy`,
  `Services.Windows/Capture/Clock/Limits/Displays` the given instances and
  `Services.Renderer` the one for `d`; used as the router factory in the
  router's worked-example rig, `Ready` starts both engines. Null arguments →
  ANE. (B48)
- **AC15** [mac] `ShortcutRelay` (router rig A/B, w1–w3; `apply` fake
  returns `Registered` for all except `Taken` for the Split combo; recording
  every call):
  - construction → no `apply` call; `router.Ready([w1,w2,w3])` → exactly one
    `apply(HotkeyPlan.Desired(Table,false))`; `LastReports` = the fake's
    result; `Applied` raised once with reason `Start`; the log has one summary
    line and one line naming the taken combo;
  - `triggers.Retry()` → second apply (reason Retry);
  - `Pressed(Win+Alt+Right)` after `WindowFocused(w1)`, `AppActivated(100,false)`
    → `CommandRoute` on `A|default` with `Move Right`; an unbound combo →
    null; while recording → null and no route;
  - an `apply` that throws → the exception propagates from the trigger call.
  (B39–B41)
- **AC16** [mac] `AppLifetime` (rig with engines A and B started, w1 parked
  on A via internal `SetParked` after pumping the start landing, recording
  `stopTracking` and `finish`):
  - `RequestQuit(User)` at t=50 → in order: triggers stopped (relay applied
    `[]`), `stopTracking` once, both engines saved then released; `State`
    ShuttingDown; `IsQuitComplete` false; `finish` not called;
  - pump the window system → `router.ShutdownCompleted` → still no `finish`
    until `clock.Advance(0)` → `finish(0)` once, `State` Finished,
    `ExitCode` 0, `Finished` raised once;
  - deadline variant (never pump): `finish(0)` at t=52.0 (router deadline,
    then `After(0)`); with a clock wrapper that drops the router's `After`
    calls, `finish` runs at t=52.5 (safety net) and `IsQuitComplete` is false
    at 52.499 and true at 52.5;
  - zero engines (before Ready): `finish` not called inside `RequestQuit`,
    called by `Advance(0)`;
  - `ReportFault(e)` while Running → quit starts with reason Fault, exit code
    1; `ReportFault` while ShuttingDown (reason User) → exit code 1;
    after Finished → only logged; 25 faults → 20 full log entries, the rest
    counted in the finish line;
  - a display store whose `Save` throws (so `router.BeginShutdown` throws) →
    `RequestQuit` does not throw, `Faulted` true, one log line with the
    exception, and `finish(1)` is called no later than 2.5 s after the
    request (safety net, B42.4);
  - second `RequestQuit` → no second save/release/stop; `finish` that throws
    → `Finished` still raised, exception logged. (B42–B47)
- **AC17** [mac] Architecture (reflection/text, from the repo root):
  - `CanvasWm.Engine` references only `CanvasWm.Core`; `Engine.csproj` and
    `Core.csproj` have no `PackageReference` and no `-windows` TFM; the
    existing `EngineReferencesOnlyCore` and Core guard tests pass;
  - no `.cs` file under `src/CanvasWm.Engine` or `src/CanvasWm.Core`
    matches `\bFile\.|\bDirectory\.|FileStream|StreamWriter|Stopwatch|new Thread\b|System\.Threading\.Timer|Task\.Delay|Thread\.Sleep|DateTime\.(Utc)?Now|Environment\.GetFolderPath|System\.Windows`;
  - no public type or member name in `CanvasWm.Engine.Hosting` or
    `CanvasWm.Engine.Persistence` contains `Gate`, `Locked`, `Lesson`,
    `Upgrade`, `Licen`, `Trial`, `Welcome` or `Onboard`;
  - no exported type is named `IStore` or `IHotkeys` (engine-interfaces AC27
    still holds). (Placement)
- **AC18** [mac] App/Win32 source checks (text, repo root):
  - `CanvasWm.App.csproj`: `net10.0-windows`, `WinExe`, `UseWindowsForms`
    true, `ApplicationManifest` `app.manifest`, `ApplicationHighDpiMode`
    `PerMonitorV2`, `WFAC010` in `NoWarn`, references exactly Core, Engine,
    Win32, no `PackageReference`;
  - `src/CanvasWm.App/app.manifest` contains `PerMonitorV2`, `true/pm` and
    `asInvoker`;
  - `src/CanvasWm.App/Program.cs` contains `[STAThread]` and
    `Application.Run(`;
  - `CanvasWm.Win32.csproj` has no `UseWindowsForms`/`UseWPF`; no file under
    `src/CanvasWm.Win32` contains `System.Windows.Forms`;
  - the #10 AC4 and AC5 scans over `src/CanvasWm.Win32` still print nothing;
  - no layout or policy in the App:
    `rg -n "\.(Move|ZoomIn|ZoomOut|ToggleOverview|ArrangeAll|ReleaseAll|VisibleAreaChanged|Suspend|Resume)\(" src/CanvasWm.App`
    prints nothing, and
    `rg -nP "(?<![\w.])(1\.5|0\.4|0\.8|2\.0)(?![\w.])" src/CanvasWm.App -g "*.cs"`
    prints nothing (router timings stay in `RouterOptions`);
  - `rg -n "TrellisWM|Licensing" src tests` prints nothing. (Placement,
    B59, B60, CLAUDE.md)
- **AC19** [mac] `dotnet build CanvasWm.slnx` exits 0 with 0 warnings on the
  Mac (WinForms App included via `EnableWindowsTargeting`);
  `dotnet test tests/CanvasWm.Core.Tests` and `tests/CanvasWm.Engine.Tests`
  pass with every earlier test unchanged. (solution-skeleton AC1)

### [windows] automated (tests/CanvasWm.Win32.Tests)

Host tests construct the window on a dedicated STA test thread and drive it
with `Win32HostWindow.PumpPending`; tests may use `SendMessageW`/`PostMessageW`
themselves.

- **AC20** [windows] Host window shape: `FindWindowW("CanvasWm.HostWindow",
  null)` finds it; `IsWindowVisible` false; `GetAncestor(h, GA_PARENT) ==
  GetDesktopWindow()` (top-level, not message-only); extended style has
  `WS_EX_TOOLWINDOW`; `GetWindowThreadProcessId` = the test thread; two host
  windows can coexist (class registered once). `Dispose` from another thread
  → `InvalidOperationException`; on the owner thread → `IsWindow` false;
  second `Dispose` no-op. (B49, B55)
- **AC21** [windows] Post semantics: an action posted on the owner thread
  has not run when `Post` returns; 1 000 actions posted from the owner thread
  run in order after pumping; 4 threads × 1 000 posts → all 4 000 run exactly
  once, on the owner thread, each thread's actions in its own order;
  `PendingCount` returns to 0. A self-reposting action plus a test
  `PostMessageW(h, WM_DISPLAYCHANGE)` → one `PumpPending(0)` returns (within
  5 s) and `DisplayChanged` was raised during it (drain yields, B50). An
  action posted during a drain runs after the current action returns, never
  inside it. After `Dispose`, `Post` does not throw and the action never runs.
  (B50, B54, B55)
- **AC22** [windows] Errors: an action that throws → `onError` receives that
  exception on the owner thread; an action posted after it still runs on a
  later pump; a `Notified` handler that throws → `onError`; an `onError` that
  throws → no crash, later posts still run. (B51)
- **AC23** [windows] Notifications (posted or sent by the test): exact
  mapping of the B52 table for `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE(0x2F)`,
  `WM_SETTINGCHANGE(0)`, `WM_DPICHANGED`, `WM_POWERBROADCAST(0x12)` (raised),
  `WM_POWERBROADCAST(0x4)` (not raised), `WM_ENDSESSION(1)` (raised),
  `WM_ENDSESSION(0)` (not raised), `WM_CLOSE` (`CloseRequested`, `IsWindow`
  still true), the registered activate message; `SendMessageW(h,
  WM_QUERYENDSESSION)` returns non-zero and raises `QueryEndSession`;
  `SendMessageW(h, WM_POWERBROADCAST, 0x12)` returns non-zero. (B52)
- **AC24** [windows] `SignalExisting()` with a live host in the test process
  → true and `InstanceActivated` raised after a pump; with no host → false.
  `PumpPending(-1)` → AOORE; `PumpPending(50)` on an idle queue returns 0
  after at least 40 ms and at most 1 s. (B53, B54)
- **AC25** [windows] Display snapshot sanity on an interactive desktop: at
  least one entry; exactly one `IsMain`, whose `Frame` has X = 0 and Y = 0;
  keys non-empty and pairwise distinct; every `WorkArea` lies within its
  `Frame`; `Dpi >= 96`; every `Frame` equals `DipSpace.ToDip(rcMonitor,
  rcMonitor, dpi)` for the monitor read independently in the test; two
  consecutive snapshots have equal `DisplayInfo.SignatureOf`; with
  DISPLAYCONFIG available, keys start with `\\?\` (logged, not asserted, if
  not — A9). (B56)
- **AC26** [windows] `DirectoryTextStore` in a fresh temp folder (path not
  yet existing): `Read` → Missing; `Write("a.json", "héllo\n")` → true, folder
  created, file bytes are exactly the UTF-8 of the text without BOM; `Read` →
  Ok with that text; a file written with a BOM by the test reads without the
  BOM; overwrite works; no `*.tmp` remains after successful writes; `Move`
  to an existing name replaces it, `Move` of a missing name → false; with the
  destination opened by the test with `FileShare.None`, `Write` → false, the
  old content survives and no `*.tmp` remains; with the file opened
  exclusively, `Read` → Failed; invalid names → `ArgumentException`; a
  relative directory → `ArgumentException`. Round-trip through
  `FileDisplayStore` + `SettingsFile.LoadOrCreate` + `FileLimitStore` in the
  temp folder gives the same results as AC8–AC10's main cases. (B58)
- **AC27** [windows] Tick primitives: `DwmComposition.Flush()` returns true
  in an interactive session; 30 consecutive flushes take between 0.1 s and
  3 s in total (sanity, not pacing); `RefreshRate()` is null or in [20,
  1000]. `HighResolutionTimer`: `Wait(0.01)` returns true after at least 9 ms
  and (on an idle machine) at most 50 ms; `Wait(0)` returns at once;
  `Wait(-1)`/`Wait(NaN)` → AOORE; after `Dispose` → `ObjectDisposedException`.
  (B67)
- **AC28** [windows] Public surface: the exported types of `CanvasWm.Win32`
  are exactly the 15 listed under Public API; no Win32 type has a member typed
  from `CanvasWm.Engine.Tracking`, `System.Windows.Forms` or
  `CanvasWm.Engine.Hosting`; `DirectoryTextStore` implements
  `ITextFileStore`. The three earlier exact-list tests are updated
  accordingly (see Required amendments). (Placement)

### [windows-manual] smoke tests (windows-verification/app-host.md)

Build `CanvasWm.App` (Release), run the exe from Explorer as a normal user.
Logs: `%LOCALAPPDATA%\CanvasWM\logs\canvaswm.log`.

- **AC29** [windows-manual] First run: a tray icon with tooltip "CanvasWM"
  appears within 3 s; no console, no window, nothing in Alt+Tab or on the
  taskbar; `settings.json` exists with 16 entries; the log has a start line
  and a hotkey summary line. (B59, B61, B65, B28)
- **AC30** [windows-manual] Tiling: with three normal app windows open
  before start, they are tiled on their display within 2 s of start; a newly
  opened window is placed; closing one re-tiles. (B61)
- **AC31** [windows-manual] Hotkeys: Win+Alt+Left/Right moves focus/framing
  between tiles; Win+Alt+Shift+Up/Down work; with the pointer and focus on
  an empty desktop area, Win+Alt+Right changes nothing visible and the app
  keeps running (no fault line in the log). (B39–B41)
- **AC32** [windows-manual] Retry shortcuts: start another tool that holds
  one CanvasWM combo (e.g. registers Win+Alt+N), then start CanvasWM → balloon
  about unregistered shortcuts, log names the combo as taken; close the
  other tool; tray → "Retry shortcuts" → log shows the combo registered and
  it works. (B40, B65)
- **AC33** [windows-manual] Persistence: rearrange, quit via tray, relaunch
  → the arrangement is restored; `layouts\arrangement-<64 hex>.json` exists
  per display and is readable JSON with an `engineKey` beginning `\\?\`.
  (B12, B13, B22, B23)
- **AC34** [windows-manual] Display changes: (a) unplug the second monitor →
  within 3 s every window is on the remaining monitor and visible; replug →
  windows are managed on both; (b) change the second monitor's scale
  (e.g. 150 % → 125 %) → within 3 s tiles fit the new work area, nothing is
  off-screen; (c) move the taskbar to the left → tiles respect the new work
  area within 1 s. Record which notifications the log shows for (b)
  (A15, `WM_DPICHANGED` [verify]). (B56, B64)
- **AC35** [windows-manual] Mixed DPI (100 % + 150 %): tiles on both monitors
  align to their work areas with no gaps or overlaps larger than 2 px.
  (CLAUDE.md mixed-DPI rule, B56)
- **AC36** [windows-manual] Quit restores: make at least one window parked
  (off-screen) by moving focus so it leaves the frame; tray → "Quit
  CanvasWM" → within 2.5 s every window is visible on a display, the process
  has exited (Task Manager), the tray icon is gone, exit code 0 (`echo
  %ERRORLEVEL%` after `start /wait`). (B42–B44, B66)
- **AC37** [windows-manual] `taskkill /IM CanvasWm.App.exe` (no `/F`) with a
  parked window → same result as AC36 [verify that WM_CLOSE reaches the
  hidden host window, A13]. `taskkill /F` → the process ends at once; the
  parked window stays off-screen (documented, #14); relaunching CanvasWM
  brings it back into the layout. (B64, B69)
- **AC38** [windows-manual] Second launch: run the exe again → it exits in
  under 1 s with code 0; the first instance shows "CanvasWM is already
  running."; only one `CanvasWm.App.exe` process remains. (B53, B59, B64)
- **AC39** [windows-manual] Sign out / restart with a parked window: the
  session ends without a "this app is preventing shutdown" screen lasting
  longer than 3 s; after signing back in and starting CanvasWM, the log of
  the previous run ends with a finish line (reason EndSession). (B64)
- **AC40** [windows-manual] Crash path: `CanvasWm.App.exe
  --diagnostic-fault=ui` with a parked window → after ~5 s windows are
  restored, process exits with code 1, the log holds the exception and a
  finish line with reason Fault. `--diagnostic-fault=background` → windows
  restored before the process ends (exit code is the runtime's crash code),
  log holds the exception. (B59, B69)
- **AC41** [windows-manual] Corrupt files: with CanvasWM stopped, replace one
  layout file and `settings.json` by the text `{"version":1` → start →
  CanvasWM runs, `*.corrupt` copies exist, `settings.json` is rewritten with
  defaults, the log names both. (B22, B28)
- **AC42** [windows-manual] Tick driver: trigger a motion that runs frames
  (e.g. a landing after Win+Alt+Right) → the frames-off log line shows source
  Composition and a mean interval within 20 % of the refresh period; on a
  machine where it shows Timer, note it in the verification file (A10).
  (B37, B67)

Count: **19 [mac]** (AC1–AC19), **9 [windows]** automated (AC20–AC28),
**14 [windows-manual]** (AC29–AC42).

## Assumptions

- **A1 (post = own queue + `PostMessage` to the host window, not
  `Control.BeginInvoke`).** *Needs human review.* Both never run inline and
  are FIFO. Chosen because: it lives in Win32 (framework-free, testable in
  Win32.Tests); one `PostMessage` per burst instead of one marshalling message
  and `IAsyncResult` per action (window-event floods); it needs no WinForms
  control created before the Win32 components; the drain yields to the loop
  between batches (B50); and the same window receives the broadcasts.
  Cost: exceptions in drained actions do not reach `Application.ThreadException`
  by themselves, so the host forwards them through `onError` (B51).
- **A2 (clock = Stopwatch + WinForms timer, ~16 ms granularity).** *Needs
  review on Windows (AC30–AC34 behaviour).* The tracker's 0.05 s rechecks and
  the router's debounces tolerate it. `After(0)` is served by a post (no timer
  delay). The scheduling logic is the pure `LoopClock` in Engine.Hosting.
- **A3 (JSON codecs and file stores in Engine.Persistence).** Pure codecs over
  Engine types, Mac-tested; file I/O only behind `ITextFileStore`. This
  supersedes engine-interfaces B15's sentence "Engine contains no JSON code"
  and fixes its A10 nested shapes (see Required amendments).
- **A4 (`DirectoryTextStore` in Win32).** It uses only `System.IO` (no
  P/Invoke), but it is the OS adapter of a seam and Win32.Tests can test it
  automatically on Windows; the App has no test project. Alternative: App
  (then AC26 becomes manual).
- **A5 (restore targets and dock spots not persisted).** No concrete
  `RestoreTarget` exists; encoding throws and the save is skipped and logged
  (B17). Nothing produces these values today. The hide/restore feature must
  extend the codec.
- **A6 (non-finite numbers as strings).** JSON has no NaN/∞; the model
  allows them in raw trees. Strings keep round-trips exact; layout-tree's
  `Sanitize` repairs them on use.
- **A7 (decode strictness).** Unknown keys ignored; wrong types, unknown
  kinds/axes, missing ids reject the whole file (layout-tree A2 suggestion).
  A rejected file is preserved as `.corrupt` once, then the engine's next save
  writes a fresh file. A future-version file is treated the same way.
- **A8 (settings scope).** Only shortcuts and the profile (`shortcuts.profile`,
  our key). The file is written on first run (so the human can edit it) and
  after a corrupt file; an existing valid file is never rewritten by #33.
  Policy, exclusions and start-at-login arrive with #34.
- **A9 (display key fallback).** DISPLAYCONFIG device path (human decision);
  fallback `\\.\DISPLAYn`, then `display-<i>`; duplicates get `#n`. A
  fallback key is not stable across reboots, so such a display may lose its
  saved layout. *Verify* on the human's PC that keys are device paths.
- **A10 (one tick driver, constants 2 ms / 5 / 60 Hz).** *Needs review.*
  Deviation from the studied per-display timers. `DwmFlush` paces to the
  composition clock; when it fails or returns suspiciously fast 5 times in a
  row the driver switches to a high-resolution waitable timer at the
  composition refresh rate, and tries `DwmFlush` again at the next frames-on
  period. Whether `DwmFlush` stalls while DWM has nothing to compose is
  checked by AC42.
- **A11 (quit without nested pumping, except end-session).** The normal loop
  runs until `Finish`; `Finish` is deferred via `After(0)` so the window
  system is never disposed inside its own completion. A cancelled end-session
  does not cancel the quit. The safety net adds 0.5 s to the router's 2.0 s.
- **A12 (background-thread crash wait).** The faulting thread blocks up to
  3.5 s so the UI thread can release windows before the runtime terminates the
  process. If the UI thread is itself stuck, the windows stay parked (#14).
- **A13 (`WM_CLOSE` = orderly quit).** `taskkill` without `/F` posts
  `WM_CLOSE` to a process's top-level windows. *Verify* that a hidden
  tool window receives it (AC37). Mirrors studied rule 4 (signals → quit).
- **A14 (DPI declared twice, analyzer warning suppressed).** The manifest
  makes PerMonitorV2 effective before any code runs (hooks, `DipSpace`,
  `FindWindow` in a second instance); `ApplicationHighDpiMode` keeps WinForms
  consistent. The WinForms analyzer WFAC010 ("DPI settings in the manifest")
  would fail the build under warnings-as-errors, so it is suppressed in the
  App csproj only. If the human prefers no suppression: drop the manifest DPI
  entries and rely on `ApplicationHighDpiMode` (called first in `Main`).
- **A15 (no extra "settle" snapshot).** The App takes one snapshot per
  notification burst; the router debounce uses the last one. If Windows
  finishes a change without a further message, the work area may be stale
  until the next one (router A3). *Verify* in AC34.
- **A16 (single instance per user session).** `Local\` mutex plus the user
  SID. An abandoned mutex counts as acquired. One `FindWindow` attempt, no
  retry; the second instance writes no log (it would share the first
  instance's file).
- **A17 (activation placeholder).** A balloon until #34 opens settings.
- **A18 (hard kill).** Human decision: ship without #14; documented in B69
  and AC37.
- **A19 (tray icon).** `SystemIcons.Application` until CanvasWM has its own
  artwork; nothing copied from the studied app.
- **A20 (durable writes, skip unchanged).** Every write is flushed to disk
  before the rename; `FileDisplayStore` skips writes of unchanged text, so the
  per-change saves of the engine cost one small write only when something
  changed. *Review* if saves show up as UI stalls on slow disks.
- **A21 (`--diagnostic-fault`).** A diagnostic switch in the shipped exe,
  needed for AC40; it does nothing unless passed. *Review* whether to keep it
  after verification.
- **A22 (no App test project).** Keeps the solution-skeleton project list.
  If the human wants automated App tests, adding `tests/CanvasWm.App.Tests`
  (`net10.0-windows`) is a separate change.

## Required amendments to earlier specs (reported, not applied)

- `specs/engine-interfaces.md` B15 / A10: the codec lives in
  `CanvasWm.Engine.Persistence` (pure, System.Text.Json from the shared
  framework, no package); nested shapes are fixed by this spec B13–B19.
- `specs/solution-skeleton.md` B13 / AC13 / A5: App now uses WinForms
  (`UseWindowsForms`), has `app.manifest`, and `Main` runs the host; AC13
  ("exits 0 promptly, no tray") is superseded by AC29–AC42 here.
- Win32 public-surface tests: `specs/win32-window-enum.md` AC15,
  `specs/win32-frame-writer.md` AC12 and the #12 hotkeys public-type count
  (9) must accept the 6 new types (15 in total). The developer updates
  `tests/CanvasWm.Win32.Tests/WindowEnum/PureHelperTests.cs`,
  `.../FrameWriter/PureHelperTests.cs` and `.../Hotkeys/PureHelperTests.cs`
  accordingly (expected-list change only).
- `specs/layout-tree.md` A1/A2: implemented as stated; the "store decides"
  cases are decided as rejections (B19).

## Traceability

| Spec item | Findings rule / decision |
|---|---|
| B1, AC1, A9 | M4; #29 decision (DISPLAYCONFIG device path); rule 9 (display key) |
| B10–B12, AC2 | rule 23 (one file per engine key); M6; #29 decisions (`%LOCALAPPDATA%\CanvasWM`, SHA-256 names) |
| B13–B19, AC3–AC7, A3, A5–A7 | rule 23; M6; engine-interfaces B15, A10; layout-tree A1, A2 |
| B20, B25, B26, AC9 | rule 24 (learned minimums persisted); M6; engine-interfaces B13 |
| B21, B27, B28, AC10, A8 | rules 24, 25 (preferences; start-at-login off); M6; shortcut-model A4; #29 decision (start-at-login off) |
| B22–B24, AC8 | rule 23 (written on every save, errors swallowed); engine-interfaces B16–B18 |
| B30–B35, AC11, A2 | M5; engine-interfaces B19 |
| B37, B38, B67, AC12, AC27, AC42, A10 | rule 22 (frame driver, late frames logged); N6; #29 decision (DwmFlush + high-res timer fallback) |
| B39–B41, AC15, AC31, AC32 | rule 5 (shortcut table after engine start); N7; router B35–B38 |
| B42–B47, AC16, AC36, A11 | rule 27 (save, release, wait ≤ 2 s, 0.02 s slices); N8; router B32–B34 |
| B48, AC13, AC14 | rule 8 (engine creation); M7 (null renderer/capture) |
| B49–B55, AC20–AC24, A1 | K1–K4 (STA thread, post, hidden top-level window); #10 B17, A13 |
| B56, B57, AC25, AC34, AC35 | rules 10–13 (display list, changes); M4; N5; CLAUDE.md mixed-DPI |
| B58, AC26, A4, A20 | M6 (atomic write, temp + rename) |
| B59, B60, AC18, AC29, AC38, A14, A16 | rules 1–4 adapted (no permission gate, no older-copy wait); N1–N4; L (WinForms); #29 decision (second launch signals and exits) |
| B61–B63, AC19, AC30 | rules 5–7; N1 |
| B64, AC34, AC37, AC39, A13, A15 | rules 4, 6c, 11; N5; N8 |
| B65, AC29, AC32, A17, A19 | rule 26 (menu; reduced to two items for #33); N7, N10 (subset) |
| B66 | rule 27d |
| B68 | N13 |
| B69, AC37, AC40, A12, A18, A21 | rule 28; N9; #29 decision (do not wait for #14) |
| AC17, AC18, AC28 | CLAUDE.md layering and clean-room rules; #10 AC4/AC5 |

## Corrections after the build gate (human-approved)

- `TickPacingOptions` gains `LateFrameFactor` (default 1.5, finite and ≥ 1, validated in its init accessor); the App's tick driver reads it instead of a local literal. Public-member checks on `TickPacingOptions` must allow it.
- `NativeDwmTimingInfo` matches `DWM_TIMING_INFO` with `Pack = 1` and all members (incl. `cRefreshesDisplayed`, `cRefreshesPresented`).
- `NoWarn` also lists `WFO0003` (same intent as A14).

## Corrections from the test stage (human-approved)

- **AC10:** `HostSettings` must validate in its `init` accessors so `with`
  copies are checked too (explicit backing fields) — fixed in the developer
  stage after the test run.
- **AC16:** with the router's `After` dropped, `router.IsShutdownComplete` is
  deadline-based and true from 52.0 (B45 ORs it in), so "`IsQuitComplete`
  false at 52.499" cannot hold; the finish still happens at 52.5. Read the AC
  as: the lifetime finishes at 52.5 and `IsQuitComplete` is true then.
- **AC17:** the name check applies to all members; the private field formerly
  named `_timeGate` in `LoopClock` is renamed `_timeLock` so no member name
  contains the excluded word.
- **AC18:** `FileLog` rotates by copy then delete (no `.Move(` in the App);
  equivalent behaviour.

