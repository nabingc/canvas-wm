namespace CanvasWm.Core.Windows;

/// <summary>Pure window classification rules.</summary>
public static class WindowClassifier
{
    /// <summary>Ignore or Managed(dockable) for a window that is not tracked yet.</summary>
    /// <param name="facts">The facts.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>The classification.</returns>
    public static WindowClassification Classify(OsWindowFacts facts, ClassifierPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(policy);

        if (facts.Handle == 0) return WindowClassification.Ignore(IgnoreReason.NoHandle);
        if (facts.IsOwnProcess) return WindowClassification.Ignore(IgnoreReason.OwnProcess);
        if (facts.IsShellWindow) return WindowClassification.Ignore(IgnoreReason.ShellWindow);
        if (IsExcluded(facts, policy)) return WindowClassification.Ignore(IgnoreReason.ExcludedApp);
        if (!facts.IsTopLevel || facts.IsChild) return WindowClassification.Ignore(IgnoreReason.NotTopLevel);
        // SPEC-ASSUMPTION A15: a standard window needs a caption or thick frame; owned needs AppWindow.
        bool standard = !facts.IsToolWindow && !facts.IsNoActivate
                        && (!facts.IsOwned || facts.IsAppWindow)
                        && (facts.HasCaption || facts.IsResizable);
        if (!standard) return WindowClassification.Ignore(IgnoreReason.NotStandard);
        if (!facts.IsVisible) return WindowClassification.Ignore(IgnoreReason.Hidden);
        if (facts.Cloak != CloakKind.None) return WindowClassification.Ignore(IgnoreReason.Cloaked);
        if (facts.IsMinimized) return WindowClassification.Ignore(IgnoreReason.Minimized);
        if (facts.IsFullScreen) return WindowClassification.Ignore(IgnoreReason.FullScreen);
        if (facts.IsHung) return WindowClassification.Ignore(IgnoreReason.Hung);
        if (!facts.IsAccessible) return WindowClassification.Ignore(IgnoreReason.Inaccessible);
        if (facts.Frame is not { } r
            || !double.IsFinite(r.X) || !double.IsFinite(r.Y)
            || !double.IsFinite(r.Width) || !double.IsFinite(r.Height))
        {
            return WindowClassification.Ignore(IgnoreReason.NoFrame);
        }

        if (r.Width < policy.MinimumSize.Width || r.Height < policy.MinimumSize.Height)
        {
            return WindowClassification.Ignore(IgnoreReason.TooSmall);
        }

        return WindowClassification.Managed(IsDockable(facts));
    }

    /// <summary>True when the window may be tiled: resizable, has a maximize button, and the
    /// maximize command is enabled or unknown.</summary>
    /// <param name="facts">The facts.</param>
    /// <returns>Whether it is dockable.</returns>
    public static bool IsDockable(OsWindowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.IsResizable && facts.HasMaximizeButton && (facts.MaximizeEnabled ?? true);
    }

    /// <summary>Fate of a tracked window; current = null means the window no longer exists.</summary>
    /// <param name="current">Current facts or null.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>The fate.</returns>
    public static TrackedFate Fate(OsWindowFacts? current, ClassifierPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (current is null) return TrackedFate.RemovePermanent;
        if (current.IsOwnProcess || current.IsShellWindow || IsExcluded(current, policy))
        {
            return TrackedFate.RemovePermanent;
        }

        // SPEC-ASSUMPTION A6: a hung window is kept while tracked.
        if (current.IsHung) return TrackedFate.Keep;
        if (current.IsMinimized) return TrackedFate.RemoveTemporary;
        if (!current.IsVisible) return TrackedFate.RemoveTemporary;
        // SPEC-ASSUMPTION A5: shell/inherited cloaks mean another virtual desktop; keep.
        if (current.Cloak == CloakKind.App) return TrackedFate.RemoveTemporary;
        if (current.Cloak is CloakKind.Shell or CloakKind.Inherited) return TrackedFate.Keep;
        // SPEC-ASSUMPTION A3/A4: a listed window that no longer qualifies is removed temporarily.
        return Classify(current, policy).IsManaged ? TrackedFate.Keep : TrackedFate.RemoveTemporary;
    }

    // SPEC-ASSUMPTION A16: exclusion matching is ordinal; a null app id is never excluded.
    private static bool IsExcluded(OsWindowFacts f, ClassifierPolicy p) =>
        f.AppId is not null && p.ExcludedApps.Contains(f.AppId);
}
