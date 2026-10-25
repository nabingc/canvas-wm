using CanvasWm.Engine.Hosting;
using CanvasWm.Win32.Hosting;

namespace CanvasWm.App;

/// <summary>Process-wide exception hooks (B69). Install once; Bind when the lifetime exists.</summary>
internal sealed class CrashHooks
{
    private readonly FileLog _log;
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly ManualResetEventSlim _finished = new(false);
    private Win32HostWindow? _host;
    private AppLifetime? _lifetime;
    private double _waitSeconds;

    internal CrashHooks(FileLog log) => _log = log;

    internal void Install()
    {
        Application.ThreadException += (_, e) => Fault(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log.Write("unobserved task exception: " + e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Connects the hooks to the running host. waitSeconds bounds how long a faulting
    /// background thread is held so the UI thread can release the windows.</summary>
    internal void Bind(Win32HostWindow host, AppLifetime lifetime, double waitSeconds)
    {
        _host = host;
        _lifetime = lifetime;
        _waitSeconds = waitSeconds;
        lifetime.Finished += () => _finished.Set();
    }

    /// <summary>Reports a fault to the lifetime, or logs it when there is none yet.</summary>
    internal void Fault(Exception e)
    {
        AppLifetime? lifetime = _lifetime;
        if (lifetime is null)
        {
            _log.Write("fault before the host was ready: " + e);
            return;
        }

        lifetime.ReportFault(e);
    }

    private void OnUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        _log.Write($"unhandled exception (terminating: {e.IsTerminating}): {e.ExceptionObject}");
        AppLifetime? lifetime = _lifetime;
        Win32HostWindow? host = _host;
        if (lifetime is null || host is null || Environment.CurrentManagedThreadId == _uiThreadId
            || lifetime.State == LifetimeState.Finished)
        {
            return;
        }

        Exception error = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString());
        host.Post(() => lifetime.ReportFault(error));
        _finished.Wait(TimeSpan.FromSeconds(_waitSeconds));
    }
}
