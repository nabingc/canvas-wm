using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Windows;

/// <summary>Raw, platform-neutral facts about one top-level OS window, as gathered by an
/// IWindowSource adapter. The adapter reports facts only and never judges them. Immutable;
/// compiler-generated value equality. Every bool defaults to false, so a default instance is
/// "nothing known" and is ignored by the classifier.</summary>
public sealed record OsWindowFacts
{
    private readonly string _title = "";

    /// <summary>OS window identity (Windows: the HWND as an unsigned integer). 0 = invalid.</summary>
    public ulong Handle { get; init; }

    /// <summary>Owning process id.</summary>
    public int ProcessId { get; init; }

    /// <summary>Owning thread id (used with ProcessId to detect a reused handle).</summary>
    public int ThreadId { get; init; }

    /// <summary>Application identity (Windows: lower-cased image path, or the AUMID for packaged
    /// apps), or null when unknown.</summary>
    public string? AppId { get; init; }

    /// <summary>Application display name, or null when unknown.</summary>
    public string? AppName { get; init; }

    /// <summary>Window title; empty when unreadable. Never null.</summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public string Title
    {
        get => _title;
        init => _title = value ?? throw new ArgumentNullException(nameof(Title));
    }

    /// <summary>Frame in DIPs (Windows: DWM extended frame bounds), or null when unreadable.</summary>
    public Rect? Frame { get; init; }

    /// <summary>The window belongs to CanvasWM's own process.</summary>
    public bool IsOwnProcess { get; init; }

    /// <summary>The window is a shell surface: the desktop or a taskbar.</summary>
    public bool IsShellWindow { get; init; }

    /// <summary>The window's process owns the desktop. Passed to AppActivated.</summary>
    public bool OwnsDesktop { get; init; }

    /// <summary>The window is its own root.</summary>
    public bool IsTopLevel { get; init; }

    /// <summary>Child-window style.</summary>
    public bool IsChild { get; init; }

    /// <summary>The window has an owner window: dialogs, property sheets, owned popups.</summary>
    public bool IsOwned { get; init; }

    /// <summary>Forced into the task switcher despite being owned.</summary>
    public bool IsAppWindow { get; init; }

    /// <summary>Tool/palette window.</summary>
    public bool IsToolWindow { get; init; }

    /// <summary>Never activated by clicks.</summary>
    public bool IsNoActivate { get; init; }

    /// <summary>Has a title bar.</summary>
    public bool HasCaption { get; init; }

    /// <summary>The user can resize it by its border.</summary>
    public bool IsResizable { get; init; }

    /// <summary>Has a maximize button.</summary>
    public bool HasMaximizeButton { get; init; }

    /// <summary>The maximize command is enabled; null = could not be read (counts as enabled).</summary>
    public bool? MaximizeEnabled { get; init; }

    /// <summary>Shown.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Compositor cloak state.</summary>
    public CloakKind Cloak { get; init; }

    /// <summary>Minimised.</summary>
    public bool IsMinimized { get; init; }

    /// <summary>Exclusive or borderless full screen. A maximised window is NOT full screen.</summary>
    public bool IsFullScreen { get; init; }

    /// <summary>The window's thread is not responding.</summary>
    public bool IsHung { get; init; }

    /// <summary>CanvasWM may move and resize it.</summary>
    public bool IsAccessible { get; init; }
}
