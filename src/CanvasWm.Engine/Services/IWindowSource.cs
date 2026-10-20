using CanvasWm.Core.Windows;

namespace CanvasWm.Engine.Services;

/// <summary>Kinds of raw window notifications.</summary>
public enum RawWindowEventKind
{
    /// <summary>A window was created.</summary>
    Created,
    /// <summary>A window was shown.</summary>
    Shown,
    /// <summary>A window was uncloaked.</summary>
    Uncloaked,
    /// <summary>A window left the minimised state.</summary>
    Restored,
    /// <summary>A window was destroyed.</summary>
    Destroyed,
    /// <summary>A window was minimised.</summary>
    Minimized,
    /// <summary>A window was hidden.</summary>
    Hidden,
    /// <summary>A window was cloaked.</summary>
    Cloaked,
    /// <summary>A window's position or size changed.</summary>
    LocationChanged,
    /// <summary>A window's title changed.</summary>
    TitleChanged,
    /// <summary>Keyboard focus moved inside or to the top-level window Handle.</summary>
    FocusChanged,
    /// <summary>Handle became the foreground window (its app is now active).</summary>
    Foreground,
}

/// <summary>One raw notification. Handle is a top-level window handle (0 = none, only
/// meaningful for Foreground/FocusChanged).</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Handle">The window handle.</param>
public readonly record struct RawWindowEvent(RawWindowEventKind Kind, ulong Handle);

/// <summary>The OS window list and its notifications. Reports facts only: no thresholds,
/// no exclusions, no removal decisions. All members are called on the engine (UI) thread.</summary>
public interface IWindowSource
{
    /// <summary>Facts for every top-level window currently in the system, frontmost first
    /// (including hidden, minimised and cloaked windows). Never null; a new snapshot each call.
    /// Never throws for OS failures (an unreadable window is omitted or reported with null fields).</summary>
    /// <returns>The facts.</returns>
    IReadOnlyList<OsWindowFacts> Enumerate();

    /// <summary>Facts for one window, or null if no window with that handle exists. Never
    /// throws for OS failures.</summary>
    /// <param name="handle">The window handle.</param>
    /// <returns>The facts or null.</returns>
    OsWindowFacts? Describe(ulong handle);

    /// <summary>Starts delivering raw events to handler. Events arrive on the engine thread,
    /// in OS order, and never synchronously inside any IWindowSource call. Disposing the result
    /// stops delivery; disposing twice does nothing.</summary>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    IDisposable Subscribe(Action<RawWindowEvent> handler);
}
