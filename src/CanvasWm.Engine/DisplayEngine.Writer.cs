using CanvasWm.Core;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine;

public sealed partial class DisplayEngine
{
    private int _passSerial;

    // ---------- tuning ----------

    /// <summary>Internal tuning constants this engine reads (refusal slack, recheck delay, ...).
    /// Default Tuning.Default. Setting it has no side effect; the new value is read at next use.</summary>
    /// <exception cref="ArgumentNullException">On set with null.</exception>
    /// <exception cref="InvalidOperationException">Set from inside one of this engine's callbacks.</exception>
    // SPEC-ASSUMPTION A1: Tuning is a public settable property, not a constructor overload.
    public Tuning Tuning
    {
        get => _tuning;
        set
        {
            EnsureNotInCallback();
            ArgumentNullException.ThrowIfNull(value);
            _tuning = value;
        }
    }

    // ---------- layout source ----------

    /// <summary>Working layout document; LayoutDocument.Empty before Start.</summary>
    internal LayoutDocument Document { get; private set; } = LayoutDocument.Empty;

    /// <summary>Id of the framed node, or null.</summary>
    internal string? FramedNodeId { get; private set; }

    /// <summary>Replace the working document and framed id. No write, no render, no save.</summary>
    /// <param name="document">The document.</param>
    /// <param name="framedNodeId">The framed node id or null.</param>
    /// <exception cref="ArgumentNullException">document is null.</exception>
    internal void SetLayout(LayoutDocument document, string? framedNodeId) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        FramedNodeId = framedNodeId;
        RefreshFramedLeaves();
        ClearZoomMemory();
    });

    // ---------- per-window limits and state ----------

    /// <summary>Set a managed window's own minimum, maximum and natural size; unknown key: nothing.</summary>
    /// <param name="key">The window.</param>
    /// <param name="minimum">Own minimum.</param>
    /// <param name="maximum">Own maximum (+infinity axis = unlimited).</param>
    /// <param name="natural">Natural size.</param>
    internal void SetLimits(WindowKey key, Size? minimum, Size? maximum, Size? natural) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_writer.TryGetValue(key, out var s)) return;
        s.Minimum = minimum;
        s.Maximum = maximum;
        s.Natural = natural;
    });

    /// <summary>Writer state of a managed window, or null if not managed.</summary>
    /// <param name="key">The window.</param>
    /// <returns>The snapshot or null.</returns>
    internal FrameWriterState? WriterState(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _writer.TryGetValue(key, out var s) ? s.Snapshot() : null;
    }

    /// <summary>True when frame matches a non-stale expected entry of key. Unknown key: false.</summary>
    /// <param name="key">The window.</param>
    /// <param name="frame">The reported frame.</param>
    /// <returns>Whether it is an echo of the engine's own move.</returns>
    internal bool IsExpected(WindowKey key, Rect frame)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_writer.TryGetValue(key, out var s)) return false;
        Prune(s);
        foreach (var e in s.Expected)
        {
            if (Near(frame, e.Frame, FrameWriting.EchoTolerance)) return true;
        }

        return false;
    }

    // ---------- stage state ----------

    /// <summary>A pointer interaction or native window drag is in progress (driven by #27/#18).
    /// Default false. No side effect.</summary>
    // SPEC-ASSUMPTION A18: a plain flag until the interaction features own it.
    internal bool InteractionActive { get; set; }

    /// <summary>True when started, not suspended, Phase is Desk, no motion and no interaction.</summary>
    internal bool CanPresentDirectly =>
        IsStarted && !IsSuspended && Phase == StagePhase.Desk && !MotionActive && !InteractionActive;

    /// <summary>Generation of the most recent landing pass; 0 before any.</summary>
    internal int PlacedGeneration { get; private set; }

    /// <summary>Clock time the most recent landing pass started; null before any.</summary>
    internal double? PlacementStarted { get; private set; }

    /// <summary>Clock time the most recent landing pass completed; null while running or abandoned.</summary>
    internal double? PlacementDone { get; private set; }

    // ---------- targets ----------

    /// <summary>Targets of all managed windows from the working document. No service call other
    /// than reading ParkingOrigin.</summary>
    /// <returns>Targets by managed window key.</returns>
    internal IReadOnlyDictionary<WindowKey, WindowTarget> ComputeTargets()
    {
        LayoutEntries entries = LayoutSolver.FocusLayout(Document.Root);
        Rect? framed = FramedNodeId is not null && entries.TryGetValue(FramedNodeId, out var fe) ? fe.Rect : null;
        var view = ProjectionView.AtRest(VisibleArea, Policy.Gap, framed);

        var facts = new Dictionary<string, WindowFacts>(StringComparer.Ordinal);
        foreach (var m in _windows)
        {
            var s = _writer[m.Info.Key];
            // SPEC-ASSUMPTION A3: LastFrameSize = ShownSize ?? size of the known frame.
            facts[m.Info.Key.Value] = new WindowFacts
            {
                LastFrameSize = s.ShownSize ?? new Size(m.Info.Frame.Width, m.Info.Frame.Height),
                Minimum = s.Minimum,
                Maximum = s.Maximum,
                Natural = s.Natural,
            };
        }

        var request = new ProjectionRequest(Document, view, ParkContext(), facts)
        {
            Entries = entries,
            Policy = new ProjectionPolicy
            {
                FixedMinimum = Policy.FixedMinimum,
                TabPadding = Policy.TabPadding,
                TabInset = Policy.TabInset,
                ParkOutOfViewSmall = Policy.ParkOutOfViewSmall,
            },
            Tuning = Tuning,
            // SPEC-ASSUMPTION A3: no desktop slot, collapsed panels or float rects until their features land.
            DesktopSlotUsable = false,
            ParkOptions = null,
        };
        var result = ProjectionTargets.Compute(request);
        var map = new Dictionary<WindowKey, WindowTarget>();
        foreach (var m in _windows)
        {
            if (result.Targets.TryGetValue(m.Info.Key.Value, out var t)) map[m.Info.Key] = t;
        }

        return map;
    }

    // ---------- presentation entry points ----------

    /// <summary>A layout edit was committed: direct presentation when at rest, else re-land.</summary>
    /// <param name="focus">Window to focus afterwards, or null.</param>
    internal void PresentLayout(WindowKey? focus = null) => Run(() =>
    {
        if (!IsStarted || IsSuspended) return;
        // SPEC-ASSUMPTION A8: Overview only renders; overview writes belong to the overview feature.
        if (Phase == StagePhase.Overview) { Render(); return; }
        if (CanPresentDirectly) PresentDirectly(focus);
        else ReLand();
    });

    /// <summary>Direct presentation: write all current targets, optionally focus, save, render once.</summary>
    /// <param name="focus">Window to focus afterwards, or null.</param>
    internal void PresentDirectly(WindowKey? focus = null) => Run(() =>
    {
        if (!IsStarted || IsSuspended) return;
        WriteCore(ComputeTargets(), null, true, null);
        if (focus is not null && focus != FocusedKey)
        {
            Services.Clock.After(0, () => Run(() =>
            {
                if (Holds(focus)) Services.Windows.Focus(focus);
            }));
        }

        Save0();
        Render();
        // SPEC-ASSUMPTION A12: direct presentation forces one OnChange even without a trigger change.
        _changeRequested = true;
    });

    /// <summary>Re-land: a landing pass for a new generation (or at once when moving or landing).</summary>
    internal void ReLand() => Run(() =>
    {
        if (!IsStarted || IsSuspended) return; // SPEC-ASSUMPTION A5: inert while suspended or not started.
        switch (Phase)
        {
            case StagePhase.Desk:
            case StagePhase.Fading:
                // SPEC-ASSUMPTION A7: the bump leaves the phase unchanged so quick re-lands coalesce.
                int g = ++PlacementGeneration;
                Services.Clock.After(FrameWriting.ReLandDelaySeconds, () => Run(() =>
                {
                    if (PlacementGeneration == g && !IsSuspended) PlaceWindows(g);
                }));
                Render();
                break;
            case StagePhase.Moving:
            case StagePhase.Landing:
                PlaceWindows(PlacementGeneration);
                Render();
                break;
            case StagePhase.Overview:
                // SPEC-ASSUMPTION A8: no pass in Overview.
                break;
        }
    });

    /// <summary>Landing pass for generation g.</summary>
    /// <param name="generation">The generation this pass belongs to.</param>
    internal void PlaceWindows(int generation) => Run(() =>
    {
        if (!IsStarted || IsSuspended) return;
        PlacedGeneration = generation;
        PlacementStarted = Services.Clock.Now;
        PlacementDone = null;
        Phase = StagePhase.Landing;
        int p = ++_passSerial;
        int remaining = -1;
        int issued = WriteCore(ComputeTargets(), null, true, () =>
        {
            if (--remaining == 0 && PlacementGeneration == generation && p == _passSerial) LandingDone(generation);
        });
        remaining = issued;
        // SPEC-ASSUMPTION A9: only the newest pass may finish the landing.
        if (issued == 0) LandingDone(generation);
    });

    private void LandingDone(int generation)
    {
        PlacementDone = Services.Clock.Now;
        FocusAfterLanding();
        Phase = StagePhase.Fading;
        Render();
        Services.Clock.After(Policy.CoverFade + FrameWriting.SettleExtraSeconds,
            () => Run(() => Settle(generation)));
    }

    private void Settle(int generation)
    {
        if (PlacementGeneration != generation || Phase != StagePhase.Fading || IsSuspended) return;
        Phase = StagePhase.Desk;
        DriftCheck(generation);
        Render();
        Save0();
    }

    // ---------- write passes ----------

    /// <summary>Batched write of the given targets; returns the number of writes issued.</summary>
    /// <param name="targets">Targets by window key; unmanaged keys are ignored.</param>
    /// <param name="except">Keys to skip, or null.</param>
    /// <param name="learns">Whether completions learn limits.</param>
    /// <returns>The number of writes issued.</returns>
    internal int WriteTargets(IReadOnlyDictionary<WindowKey, WindowTarget> targets,
                              IReadOnlySet<WindowKey>? except = null, bool learns = true)
    {
        ArgumentNullException.ThrowIfNull(targets);
        int n = 0;
        Run(() => n = WriteCore(targets, except, learns, null));
        return n;
    }

    /// <summary>Single-window write through SetFrame; true if a write was issued.</summary>
    /// <param name="key">The window.</param>
    /// <param name="target">The target.</param>
    /// <param name="learns">Whether the completion learns limits.</param>
    /// <returns>Whether a write was issued.</returns>
    internal bool WriteOne(WindowKey key, WindowTarget target, bool learns = true)
    {
        ArgumentNullException.ThrowIfNull(key);
        bool issued = false;
        Run(() =>
        {
            if (!IsStarted || IsSuspended) return;
            if (!_writer.TryGetValue(key, out var s) || ShouldSkip(key, s, target, null)) return;
            BeforeWrite(key, s, target);
            bool isPark = IsParkTarget(target);
            Services.Windows.SetFrame(key, target.Rect, a => Run(() => Complete(s, key, target, isPark, learns, a)));
            issued = true;
        });
        return issued;
    }

    private int WriteCore(IReadOnlyDictionary<WindowKey, WindowTarget> targets, IReadOnlySet<WindowKey>? except,
                          bool learns, Action? onEntryDone)
    {
        // SPEC-ASSUMPTION A19: writes skip when suspended or not started.
        if (!IsStarted || IsSuspended) return 0;
        var writes = new List<FrameWrite>();
        var entries = new List<(WindowKey Key, WriterRecord State, WindowTarget Target, bool IsPark)>();
        foreach (var m in _windows.ToArray())
        {
            var k = m.Info.Key;
            if (!targets.TryGetValue(k, out var t)) continue;
            var s = _writer[k];
            if (ShouldSkip(k, s, t, except)) continue;
            BeforeWrite(k, s, t);
            writes.Add(new FrameWrite(k, t.Rect));
            entries.Add((k, s, t, IsParkTarget(t)));
        }

        if (writes.Count == 0) return 0;
        // SPEC-ASSUMPTION A4: a full pass always uses SetFrames, even with one entry.
        Services.Windows.SetFrames(writes, results => Run(() =>
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                Rect? actual = i < results.Count ? results[i].Actual : null;
                Complete(e.State, e.Key, e.Target, e.IsPark, learns, actual);
                onEntryDone?.Invoke();
            }
        }));
        return writes.Count;
    }

    private static bool IsParkTarget(WindowTarget t) => t.Kind is TargetKind.Park or TargetKind.ParkWithPicture;

    private bool ShouldSkip(WindowKey k, WriterRecord s, WindowTarget t, IReadOnlySet<WindowKey>? except)
    {
        if (except is not null && except.Contains(k)) return true;
        var m = Window(k)!;
        Rect known = m.Info.Frame;
        if (IsParkTarget(t) && m.Parked
            && ParkGeometry.RestsParked(known, t.Rect, Services.Windows.VisibleFraction(known))) return true;
        if (Near(known, t.Rect, FrameWriting.SkipTolerance)) return true;
        if (s.Requested == t.Rect && s.WritesInFlight > 0) return true;
        return false;
    }

    private void BeforeWrite(WindowKey k, WriterRecord s, WindowTarget t)
    {
        // SPEC-ASSUMPTION A17: the writer sets the parked flag before each write.
        SetParked(k, IsParkTarget(t));
        if (t.Kind == TargetKind.Tile) s.ShownSize = new Size(t.Rect.Width, t.Rect.Height);
        s.Requested = t.Rect;
        Prune(s);
        s.Expected.Add(new ExpectedFrame(t.Rect, Services.Clock.Now));
        s.WritesInFlight++;
    }

    // ---------- completions ----------

    private bool Same(WriterRecord s, WindowKey k) => _writer.TryGetValue(k, out var cur) && ReferenceEquals(cur, s);

    private void SetKnown(WindowKey k, Rect frame)
    {
        int i = IndexOf(k);
        if (i >= 0) _windows[i] = _windows[i] with { Info = _windows[i].Info with { Frame = frame } };
    }

    private void Prune(WriterRecord s)
    {
        double now = Services.Clock.Now;
        s.Expected.RemoveAll(e => now - e.Time > FrameWriting.EchoWindowSeconds);
    }

    private static bool Near(Rect a, Rect b, double tol) =>
        Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol
        && Math.Abs(a.Width - b.Width) <= tol && Math.Abs(a.Height - b.Height) <= tol;

    private void Complete(WriterRecord s, WindowKey k, WindowTarget target, bool isPark, bool learns, Rect? actual)
    {
        s.WritesInFlight--;
        if (actual is not { } a) return;
        if (!Same(s, k)) return;
        Prune(s);
        s.Expected.Add(new ExpectedFrame(a, Services.Clock.Now));
        SetKnown(k, a);
        if (!learns) return;

        Rect t = target.Rect;
        double slack = Tuning.FrameRefusalSlack;
        if (isPark)
        {
            if (a.Width >= t.Width - FrameWriting.AcceptedParkSlack && a.Height >= t.Height - FrameWriting.AcceptedParkSlack)
                LowerMinimum(k, s, new Size(a.Width, a.Height));
            if (a.Width > t.Width + slack || a.Height > t.Height + slack) ScheduleRecheck(s, k, true, t);
            return;
        }

        if (s.Minimum is { } min
            && (a.Width < min.Width - FrameWriting.OwnMinimumSlack || a.Height < min.Height - FrameWriting.OwnMinimumSlack))
        {
            s.Minimum = new Size(Math.Min(min.Width, a.Width), Math.Min(min.Height, a.Height));
        }

        Services.Limits.Observe(Window(k)!.Info.App, new Size(a.Width, a.Height));
        if (a.Width > t.Width + slack || a.Height > t.Height + slack) ScheduleRecheck(s, k, true, t);
        else if (a.Width < t.Width - slack || a.Height < t.Height - slack) ScheduleRecheck(s, k, false, t);
    }

    private void LowerMinimum(WindowKey k, WriterRecord s, Size size)
    {
        var app = Window(k)!.Info.App;
        var before = Services.Limits.Minimum(app);
        Services.Limits.Observe(app, size);
        var after = Services.Limits.Minimum(app);
        // SPEC-ASSUMPTION A16: the store-minimum re-land is kept for parity.
        if (s.Minimum is null && before is { } b && after is { } af && b != af) ReLand();
    }

    // ---------- refusal recheck ----------

    private void ScheduleRecheck(WriterRecord s, WindowKey k, bool larger, Rect target)
    {
        s.PendingRechecks++;
        Rect visible = VisibleArea;
        Services.Clock.After(Tuning.FrameRefusalRecheckSeconds, () => Run(() => FireRecheck(s, k, larger, target, visible)));
    }

    private void FireRecheck(WriterRecord s, WindowKey k, bool larger, Rect target, Rect visible)
    {
        // SPEC-ASSUMPTION A6: a suspended or orphaned recheck makes no read.
        if (IsSuspended || !Same(s, k))
        {
            s.PendingRechecks--;
            return;
        }

        Services.Windows.ReadFrame(k, r => Run(() =>
        {
            s.PendingRechecks--;
            if (!Same(s, k) || IsSuspended) return;
            Rect a = r ?? Window(k)!.Info.Frame;
            SetKnown(k, a);
            if (larger) LearnLarger(s, k, a, target, visible);
            else LearnSmaller(s, k, a, target);
        }));
    }

    private void LearnLarger(WriterRecord s, WindowKey k, Rect a, Rect t, Rect visible)
    {
        double slack = Tuning.FrameRefusalSlack;
        bool qw = a.Width > t.Width + slack && a.Width < FrameWriting.RefusalScreenFactor * visible.Width;
        bool qh = a.Height > t.Height + slack && a.Height < FrameWriting.RefusalScreenFactor * visible.Height;
        if (!qw && !qh) return;
        double ow = s.Minimum?.Width ?? 0, oh = s.Minimum?.Height ?? 0;
        var min = new Size(qw ? Math.Max(ow, a.Width) : ow, qh ? Math.Max(oh, a.Height) : oh);
        s.Minimum = min;
        if (s.Natural is { } nat)
        {
            s.Natural = new Size(qw ? Math.Max(nat.Width, a.Width) : nat.Width,
                                 qh ? Math.Max(nat.Height, a.Height) : nat.Height);
        }

        Services.Limits.Learn(Window(k)!.Info.App, min);
        LogEvent($"minimum learned for {k.Value}");
        AfterLearning(k, t);
    }

    private void LearnSmaller(WriterRecord s, WindowKey k, Rect a, Rect t)
    {
        double slack = Tuning.FrameRefusalSlack;
        bool qw = a.Width < t.Width - slack;
        bool qh = a.Height < t.Height - slack;
        if (!qw && !qh) return;
        // SPEC-ASSUMPTION A14: an unlimited maximum axis is +infinity.
        s.Maximum = new Size(qw ? a.Width : s.Maximum?.Width ?? double.PositiveInfinity,
                             qh ? a.Height : s.Maximum?.Height ?? double.PositiveInfinity);
        LogEvent($"maximum learned for {k.Value}");
        AfterLearning(k, t);
    }

    private void AfterLearning(WindowKey k, Rect t)
    {
        var targets = ComputeTargets();
        if (targets.TryGetValue(k, out var n)
            && Near(n.Rect, t, FrameWriting.RetargetTolerance))
        {
            LogEvent($"refusal kept for {k.Value}");
            return;
        }

        ReLand();
    }

    private void LogEvent(string text) => Enqueue(() => _log?.Invoke(text));

    // ---------- drift check ----------

    private void DriftCheck(int generation)
    {
        var targets = ComputeTargets();
        foreach (var m in _windows.ToArray())
        {
            var k = m.Info.Key;
            if (!targets.TryGetValue(k, out var target)) continue;
            var s = _writer[k];
            Services.Windows.ReadFrame(k, r => Run(() =>
            {
                if (r is not { } frame || PlacementGeneration != generation || Phase != StagePhase.Desk
                    || IsSuspended || InteractionActive || !Same(s, k) || s.WritesInFlight != 0) return;
                if (IsParkTarget(target))
                {
                    double fraction = Services.Windows.VisibleFraction(frame);
                    if (fraction > ParkOptions.Default.VisibleFractionThreshold
                        && !ParkGeometry.RestsParked(frame, target.Rect, fraction))
                    {
                        SetKnown(k, frame);
                        WriteOne(k, target);
                    }
                }
                else
                {
                    Rect known = Window(k)!.Info.Frame;
                    if (Math.Abs(frame.X - known.X) > FrameWriting.DriftTileLogThreshold
                        || Math.Abs(frame.Y - known.Y) > FrameWriting.DriftTileLogThreshold
                        || Math.Abs(frame.Width - known.Width) > FrameWriting.DriftTileLogThreshold
                        || Math.Abs(frame.Height - known.Height) > FrameWriting.DriftTileLogThreshold)
                    {
                        LogEvent($"tile drift for {k.Value}");
                    }
                }
            }));
        }
    }
}
