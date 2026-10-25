using CanvasWm.Core.Geometry;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Routing;

public sealed partial class EngineRouter
{
    // ---- display changes (B14-B18) ----

    /// <summary>A new display snapshot from the platform (B14-B21).</summary>
    /// <param name="displays">The snapshot.</param>
    /// <exception cref="ArgumentNullException">displays or an element is null.</exception>
    /// <exception cref="ArgumentException">Two displays share a key.</exception>
    public void DisplaysChanged(IReadOnlyList<DisplayInfo> displays)
    {
        List<DisplayInfo> pending = ValidateDisplays(displays);
        if (IsShuttingDown) return;
        int g = ++_generation;
        _latest = pending;
        if (IsTornDown || DisplayInfo.SignatureOf(pending) != _appliedSignature)
        {
            IsTornDown = true;
            RecomputeFrames();
            _clock.After(Options.DisplaySetChangeDelaySeconds, () =>
            {
                if (g == _generation && !IsShuttingDown) Rebuild(pending, g);
            });
        }
        else
        {
            // SPEC-ASSUMPTION A3: one work-area refresh, not two.
            _clock.After(Options.WorkAreaRefreshDelaySeconds, () =>
            {
                if (g == _generation && !IsShuttingDown) RefreshWorkAreas(pending);
            });
        }
    }

    private void RefreshWorkAreas(List<DisplayInfo> pending)
    {
        _applied = pending;
        foreach (DisplayInfo d in pending)
        {
            foreach (DisplayEngine e in _engines.ToArray())
            {
                if (e.DisplayKey == d.Key) e.VisibleAreaChanged(d.WorkArea);
            }
        }
    }

    private void Rebuild(List<DisplayInfo> pending, int g)
    {
        Rect? fallback = FallbackArea(pending);
        List<DisplayEngine> engines = _engines.ToArray().ToList();
        foreach (DisplayEngine e in engines)
        {
            e.ReleaseAll(Contains(pending, e.DisplayKey) ? null : fallback);
        }

        foreach (DisplayEngine e in engines)
        {
            e.OnNeedsFrames = null;
            e.OnChange = null;
            e.Log = null;
            _engines.Remove(e);
            _startedBefore.Remove(e);
            EngineRetired?.Invoke(e);
        }

        RecomputeFrames();
        var previous = new Dictionary<DisplayKey, string>(_desktops);
        _applied = pending;
        _appliedSignature = DisplayInfo.SignatureOf(pending);
        _desktops.Clear();
        foreach (DisplayInfo d in pending)
        {
            _desktops[d.Key] = previous.TryGetValue(d.Key, out string? s) ? s : EngineKey.SingleDesktopId;
        }

        if (engines.Count > 0) Changed?.Invoke();
        _clock.After(Options.ReshowDelaySeconds, () =>
        {
            if (g != _generation || IsShuttingDown) return;
            IsTornDown = false;
            RecomputeFrames();
            ShowAll();
        });
    }

    // ---- desktop seam (B22) ----

    /// <summary>SEAM for #35: the OS now shows desktopId on display (B22).</summary>
    /// <param name="display">The display.</param>
    /// <param name="desktopId">The desktop id.</param>
    /// <exception cref="ArgumentNullException">display or desktopId is null.</exception>
    /// <exception cref="ArgumentException">desktopId empty or contains EngineKey.Separator.</exception>
    public void DesktopShown(DisplayKey display, string desktopId)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentException.ThrowIfNullOrEmpty(desktopId);
        if (desktopId.Contains(EngineKey.Separator))
        {
            throw new ArgumentException("Desktop id must not contain the separator.", nameof(desktopId));
        }

        if (IsShuttingDown || !_desktops.TryGetValue(display, out string? current) || current == desktopId) return;
        EngineFor(display)?.Suspend();
        _desktops[display] = desktopId;
        if (IsReady && !IsTornDown)
        {
            DisplayInfo? d = _applied.Find(x => x.Key == display);
            if (d is not null) ShowOne(d);
        }
    }

    // ---- commands (B25-B28) ----

    /// <summary>Executes a shortcut command on its target engine (B25-B28); null = nothing ran.</summary>
    /// <param name="command">The command.</param>
    /// <returns>What was done, or null when no engine was targeted.</returns>
    public CommandRoute? Execute(ShortcutCommand command)
    {
        if (IsShuttingDown) return null;
        Point p = _windows.Pointer;
        TargetEngine kind = command.Action is { } a ? ShortcutCatalog.Info(a).Target : TargetEngine.Active;
        DisplayEngine? target;
        Point? zoomToward = null;
        switch (kind)
        {
            case TargetEngine.PointerThenActive:
                if (StepTowardPointer && EngineAt(p) is { } under)
                {
                    target = under;
                    zoomToward = under.Phase == StagePhase.Overview ? null : p;
                }
                else
                {
                    target = ActiveEngine;
                }

                break;
            case TargetEngine.Pointer:
                target = EngineAt(p);
                break;
            default:
                target = ActiveEngine;
                break;
        }

        if (target is null) return null;
        WindowKey? splitKey = null;
        bool call = true;
        if (command.Kind == CommandKind.SplitForNewWindow)
        {
            // SPEC-ASSUMPTION A5: Split passes the pointer engine's focused key; no call without one.
            splitKey = target.FocusedKey;
            call = splitKey is not null;
        }

        var route = new CommandRoute(target.Key, command,
                                     command.Kind == CommandKind.ZoomIn ? zoomToward : null, splitKey);
        if (!call) return route;
        if (CommandInvoker is { } invoker)
        {
            invoker(target, route);
            return route;
        }

        switch (command.Kind)
        {
            case CommandKind.ToggleOverview: target.ToggleOverview(); break;
            case CommandKind.ZoomOut: target.ZoomOut(); break;
            case CommandKind.ZoomIn: target.ZoomIn(route.ZoomToward); break;
            case CommandKind.Move: target.Move(command.Toward!.Value); break;
            case CommandKind.Back: target.Back(); break;
            case CommandKind.Forward: target.Forward(); break;
            case CommandKind.ToggleMaximize: target.ToggleMaximize(null); break;
            case CommandKind.ToggleDesktopHidden: target.ToggleDesktopHidden(); break;
            case CommandKind.SplitForNewWindow: target.SplitForNewWindow(splitKey!); break;
            case CommandKind.GoToView: target.GoToFraming(command.Slot!.Value); break;
            case CommandKind.SaveView: target.SaveFraming(command.Slot!.Value); break;
        }

        return route;
    }

    // ---- quit (B32-B34) ----

    /// <summary>Quit sequence (B32-B34). Second call: no-op.</summary>
    public void BeginShutdown()
    {
        if (IsShuttingDown) return;
        IsShuttingDown = true;
        _generation++;
        RecomputeFrames();
        List<DisplayEngine> engines = _engines.ToList();
        foreach (DisplayEngine e in engines) e.Save();

        // SPEC-ASSUMPTION A7: each engine releases into its own area when its display is still
        // present in the latest snapshot, else into the latest snapshot's fallback area.
        Rect? fallback = FallbackArea(_latest);
        _deadline = _clock.Now + Options.ShutdownTimeoutSeconds;
        _clock.After(Options.ShutdownTimeoutSeconds, Complete);
        int remaining = engines.Count;
        foreach (DisplayEngine e in engines)
        {
            e.ReleaseAll(Contains(_latest, e.DisplayKey) ? null : fallback, () =>
            {
                if (--remaining == 0) Complete();
            });
        }

        if (engines.Count == 0) Complete();
    }

    private void Complete()
    {
        if (_complete) return;
        _complete = true;
        ShutdownCompleted?.Invoke();
    }
}
