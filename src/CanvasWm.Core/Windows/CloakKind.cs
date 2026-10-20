namespace CanvasWm.Core.Windows;

/// <summary>How a window is hidden by the compositor. Windows meanings: Shell = cloaked by the
/// shell (another virtual desktop, or shell-hidden); App = cloaked by its own app (for example a
/// suspended packaged app); Inherited = inherits its owner's cloak.</summary>
public enum CloakKind
{
    /// <summary>Not cloaked.</summary>
    None = 0,
    /// <summary>Cloaked by the shell.</summary>
    Shell = 1,
    /// <summary>Cloaked by its own app.</summary>
    App = 2,
    /// <summary>Inherits its owner's cloak.</summary>
    Inherited = 3,
}
