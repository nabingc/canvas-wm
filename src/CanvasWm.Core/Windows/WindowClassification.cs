namespace CanvasWm.Core.Windows;

/// <summary>Why a window is ignored. The values are listed in check order.</summary>
public enum IgnoreReason
{
    /// <summary>Not ignored.</summary>
    None = 0,
    /// <summary>Handle is zero.</summary>
    NoHandle,
    /// <summary>Belongs to our own process.</summary>
    OwnProcess,
    /// <summary>Shell surface.</summary>
    ShellWindow,
    /// <summary>App is on the exclusion list.</summary>
    ExcludedApp,
    /// <summary>Not a top-level window.</summary>
    NotTopLevel,
    /// <summary>Not a standard window (tool, no-activate, owned, no frame).</summary>
    NotStandard,
    /// <summary>Not visible.</summary>
    Hidden,
    /// <summary>Cloaked.</summary>
    Cloaked,
    /// <summary>Minimised.</summary>
    Minimized,
    /// <summary>Full screen.</summary>
    FullScreen,
    /// <summary>Not responding.</summary>
    Hung,
    /// <summary>Cannot be moved by us.</summary>
    Inaccessible,
    /// <summary>No readable finite frame.</summary>
    NoFrame,
    /// <summary>Smaller than the minimum size.</summary>
    TooSmall,
}

/// <summary>Result of classifying a candidate: Ignore(reason) or Managed(dockable).</summary>
public readonly record struct WindowClassification
{
    private WindowClassification(bool isManaged, bool dockable, IgnoreReason reason)
    {
        IsManaged = isManaged;
        Dockable = dockable;
        Reason = reason;
    }

    /// <summary>A managed window.</summary>
    /// <param name="dockable">Whether it may be tiled.</param>
    /// <returns>The classification.</returns>
    public static WindowClassification Managed(bool dockable) => new(true, dockable, IgnoreReason.None);

    /// <summary>An ignored window.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The classification.</returns>
    /// <exception cref="ArgumentOutOfRangeException">reason is None or undefined.</exception>
    public static WindowClassification Ignore(IgnoreReason reason)
    {
        if (reason == IgnoreReason.None || !Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Must be a defined reason other than None.");
        }

        return new(false, false, reason);
    }

    /// <summary>True when the window is managed.</summary>
    public bool IsManaged { get; }

    /// <summary>True when a managed window may be tiled.</summary>
    public bool Dockable { get; }

    /// <summary>Why the window is ignored; None when managed.</summary>
    public IgnoreReason Reason { get; }
}
