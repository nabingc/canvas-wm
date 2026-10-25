using System.Diagnostics;
using CanvasWm.Core.Windows;
using CanvasWm.Engine;
using CanvasWm.Engine.Hosting;
using CanvasWm.Engine.Persistence;
using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Tracking;
using CanvasWm.Win32.Displays;
using CanvasWm.Win32.Hotkeys;
using CanvasWm.Win32.Hosting;
using CanvasWm.Win32.Storage;
using CanvasWm.Win32.Windows;

namespace CanvasWm.App;

/// <summary>Which deliberate fault the manual crash test asks for (A21).</summary>
internal enum DiagnosticFault
{
    None = 0,
    Ui = 1,
    Background = 2,
}

/// <summary>Composition root and process lifetime glue (B61-B66). UI thread only.</summary>
internal sealed class HostContext : ApplicationContext
{
    private readonly FileLog _fileLog;
    private readonly CrashHooks _hooks;
    private readonly List<IDisposable> _created = [];
    private Win32HostWindow? _host;
    private UiWake? _wake;
    private LoopClock? _clock;
    private TrackerWindowMap? _map;
    private Win32WindowSystem? _windowSystem;
    private Win32WindowSource? _source;
    private EngineRouter? _router;
    private WindowTracker? _tracker;
    private Win32HotkeySource? _hotkeys;
    private HotkeyTriggers? _triggers;
    private ShortcutRelay? _relay;
    private AppLifetime? _lifetime;
    private TickDriver? _tickDriver;
    private TrayMenu? _tray;
    private bool _snapshotPending;
    private bool _disposed;

    private HostContext(FileLog fileLog, CrashHooks hooks)
    {
        _fileLog = fileLog;
        _hooks = hooks;
    }

    /// <summary>Process exit code once the message loop ended.</summary>
    internal int ExitCode { get; private set; }

    /// <summary>Composes steps 1-10 of B61. Null after a startup failure (B63): everything created
    /// is disposed, one message box was shown and the caller exits with code 2.</summary>
    internal static HostContext? Create(FileLog fileLog, CrashHooks hooks, DiagnosticFault diagnostic)
    {
        var ctx = new HostContext(fileLog, hooks);
        try
        {
            ctx.Compose(diagnostic);
            return ctx;
        }
        catch (Exception e)
        {
            fileLog.Write("startup failed: " + e);
            ctx.DisposeCreated();
            MessageBox.Show("CanvasWM could not start. Details are in the log:\n" + fileLog.FilePath,
                "CanvasWM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    /// <summary>Step 11 of B61: tracking starts, the router shows desktops, hotkeys register.
    /// A failure here is a fault (B63): the quit sequence runs in the message loop.</summary>
    internal void Begin()
    {
        try
        {
            _tracker!.Start();
        }
        catch (Exception e)
        {
            _lifetime!.ReportFault(e);
        }
    }

    private void Log(string text) => _fileLog.Write(text);

    private void Compose(DiagnosticFault diagnostic)
    {
        Action<string> log = Log;

        // 1. Host window and post queue.
        _host = new Win32HostWindow(OnHostError, log);
        _created.Add(_host);
        Win32HostWindow host = _host;
        Action<Action> post = host.Post;

        // 2. Clock.
        _wake = new UiWake(post);
        _created.Add(_wake);
        long origin = Stopwatch.GetTimestamp();
        _clock = new LoopClock(() => Stopwatch.GetElapsedTime(origin).TotalSeconds, _wake.Schedule);
        _wake.Bind(_clock.RunDue);

        // 3. Stores and settings.
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFiles.DataFolderName);
        var layouts = new DirectoryTextStore(Path.Combine(root, DataFiles.LayoutsFolderName));
        var rootFiles = new DirectoryTextStore(root);
        var displayStore = new FileDisplayStore(layouts, log);
        var limitStore = new FileLimitStore(rootFiles, log);
        HostSettings settings = SettingsFile.LoadOrCreate(rootFiles, log);

        // 4. Window system and source.
        _map = new TrackerWindowMap();
        _windowSystem = new Win32WindowSystem(post, _map, log);
        _created.Add(_windowSystem);
        _source = new Win32WindowSource(post);
        _created.Add(_source);

        // 5. Router.
        _router = new EngineRouter(
            Win32Displays.Snapshot(),
            HostComposition.EngineFactory(EnginePolicy.Default, _windowSystem, NullCapture.Instance, _clock,
                limitStore, displayStore, _ => NullRenderer.Instance),
            _clock, _windowSystem);
        // SPEC-ASSUMPTION A8: no policy settings are read in this build; the router steps toward the pointer.
        _router.StepTowardPointer = true;

        // 6. Tracker.
        _tracker = new WindowTracker(_source, _windowSystem, _clock, _router, ClassifierPolicy.Default);
        _map.Tracker = _tracker;

        // 7. Hotkeys and relay (before tracker.Start, B62).
        _hotkeys = new Win32HotkeySource(post);
        _created.Add(_hotkeys);
        _triggers = new HotkeyTriggers(settings.Shortcuts);
        _relay = new ShortcutRelay(_triggers, _router, _hotkeys.Apply, log);
        _hotkeys.Pressed += c => _relay.Pressed(c);

        // 8. Lifetime.
        _lifetime = new AppLifetime(_router, _triggers, _clock, _tracker.Dispose, Finish, log);

        // 9. Tick driver and logging.
        _tickDriver = new TickDriver(_router, _clock, post, log);
        _created.Add(_tickDriver);
        _router.FramesNeededChanged += _tickDriver.SetRunning;
        _router.Log += log;
        _router.EngineRetired += e => log("engine retired: " + e.Key);

        // 10. Tray and notifications.
        _tray = new TrayMenu(() => _triggers.Retry(), () => _lifetime.RequestQuit(QuitReason.User),
            () => _lifetime.State == LifetimeState.Running);
        _created.Add(_tray);
        host.Notified += OnNotified;
        _relay.Applied += OnApplied;

        _hooks.Bind(host, _lifetime, _router.Options.ShutdownTimeoutSeconds + AppLifetime.GraceSeconds + 1.0);
        ArmDiagnostic(diagnostic);
    }

    private void ArmDiagnostic(DiagnosticFault diagnostic)
    {
        // SPEC-ASSUMPTION A21: a diagnostic switch for the manual crash test; it does nothing unless passed.
        const double DelaySeconds = 5;
        if (diagnostic == DiagnosticFault.Ui)
        {
            _clock!.After(DelaySeconds, () => _host!.Post(() => throw new InvalidOperationException("diagnostic fault (ui)")));
        }
        else if (diagnostic == DiagnosticFault.Background)
        {
            _clock!.After(DelaySeconds, () =>
            {
                var t = new Thread(() => throw new InvalidOperationException("diagnostic fault (background)"))
                {
                    IsBackground = true,
                };
                t.Start();
            });
        }
    }

    private void OnHostError(Exception e) => _hooks.Fault(e);

    private void PostSnapshot()
    {
        if (_snapshotPending)
        {
            return;
        }

        _snapshotPending = true;
        _host!.Post(() =>
        {
            _snapshotPending = false;
            _router!.DisplaysChanged(Win32Displays.Snapshot());
        });
    }

    private void OnNotified(HostNotification n)
    {
        switch (n)
        {
            case HostNotification.DisplayChanged:
            case HostNotification.SettingChanged:
            case HostNotification.DpiChanged:
                PostSnapshot();
                break;
            case HostNotification.PowerResumed:
                PostSnapshot();
                _tracker!.Rescan();
                break;
            case HostNotification.QueryEndSession:
                _lifetime!.RequestQuit(QuitReason.EndSession);
                break;
            case HostNotification.EndSession:
                _lifetime!.RequestQuit(QuitReason.EndSession);
                while (!_lifetime.IsQuitComplete)
                {
                    Win32HostWindow.PumpPending(20);
                }

                break;
            case HostNotification.CloseRequested:
                _lifetime!.RequestQuit(QuitReason.User);
                break;
            case HostNotification.InstanceActivated:
                // SPEC-ASSUMPTION A17: placeholder until the settings window exists.
                Log("second instance signalled this one");
                _tray!.ShowBalloon("CanvasWM is already running.");
                break;
        }
    }

    private void OnApplied(HotkeyApplyRequest req, IReadOnlyList<CanvasWm.Core.Shortcuts.HotkeyReport> reports)
    {
        if (req.Reason is not (HotkeyApplyReason.Start or HotkeyApplyReason.Retry))
        {
            return;
        }

        int failed = reports.Count(r => !r.IsActive);
        if (failed > 0)
        {
            _tray!.ShowBalloon($"Some shortcuts could not be registered ({failed}). Close the program using them, then choose Retry shortcuts.");
        }
    }

    private void Finish(int code)
    {
        Guard("tick driver", () => _tickDriver?.Dispose());
        Guard("hotkeys", () => _hotkeys?.Dispose());
        Guard("window source", () => _source?.Dispose());
        Guard("window system", () => _windowSystem?.Dispose());
        Guard("tray", () => _tray?.Dispose());
        ExitCode = code;
        ExitThread();
    }

    private void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log($"finish: {what} failed: {e}");
        }
    }

    private void DisposeCreated()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
        {
            Guard("dispose", _created[i].Dispose);
        }

        _created.Clear();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            DisposeCreated();
        }

        base.Dispose(disposing);
    }
}
