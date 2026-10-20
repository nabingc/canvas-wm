using System.Collections.Immutable;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Navigation;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine;

public sealed partial class DisplayEngine
{
    private IIdGenerator _ids = UidGenerator.Shared;
    private IReadOnlyList<string> _framedLeaves = Array.Empty<string>();
    private readonly List<string?> _framingHistory = new();

    // ---------- ids and stage ----------

    /// <summary>Id generator for layout edits. Default UidGenerator.Shared. Tests inject a
    /// deterministic one. Setting it has no side effect.</summary>
    /// <exception cref="ArgumentNullException">On set with null.</exception>
    /// <exception cref="InvalidOperationException">Set from inside one of this engine's callbacks.</exception>
    internal IIdGenerator Ids
    {
        get => _ids;
        set
        {
            EnsureNotInCallback();
            ArgumentNullException.ThrowIfNull(value);
            _ids = value;
        }
    }

    /// <summary>ScreenProjection.Stage(VisibleArea, Policy.Gap).</summary>
    internal Rect Stage => ScreenProjection.Stage(VisibleArea, Policy.Gap);

    /// <summary>Stage.Width / Stage.Height (plain IEEE division; may be NaN or infinity).</summary>
    internal double StageAspect => Stage.Width / Stage.Height;

    // ---------- navigator ----------

    /// <summary>Leaf ids of the framed node as of the last navigator sync; empty when nothing is framed.</summary>
    internal IReadOnlyList<string> FramedLeaves => _framedLeaves;

    /// <summary>Previous framed ids, oldest first, appended by recorded framing changes.
    /// Cleared by Start. A simple list until #15.</summary>
    internal IReadOnlyList<string?> FramingHistory => _framingHistory.ToArray();

    /// <summary>True between an app-resize zoom and the next framing change.</summary>
    internal bool ZoomedAlone { get; private set; }

    /// <summary>Framed id to return to on un-zoom; meaningful only while ZoomedAlone.</summary>
    internal string? ZoomReturnNodeId { get; private set; }

    /// <summary>Change the framed node (null = frame nothing). Updates FramedLeaves. When
    /// record is true and the id changes, the previous FramedNodeId is appended to
    /// FramingHistory. Clears the zoom memory. No write, no render, no save.</summary>
    /// <param name="nodeId">The node to frame, or null.</param>
    /// <param name="record">Whether to record the previous framing in the history.</param>
    internal void FrameNode(string? nodeId, bool record) => Run(() =>
    {
        if (record && nodeId != FramedNodeId) _framingHistory.Add(FramedNodeId);
        FramedNodeId = nodeId;
        RefreshFramedLeaves();
        ClearZoomMemory();
    });

    private void RefreshFramedLeaves()
    {
        var entries = LayoutSolver.FocusLayout(Document.Root);
        _framedLeaves = FramedNodeId is not null && entries.TryGetValue(FramedNodeId, out var e)
            ? LayoutEdits.LeafIds(e.Node)
            : ImmutableArray<string>.Empty;
    }

    private void ClearZoomMemory()
    {
        ZoomedAlone = false;
        ZoomReturnNodeId = null;
    }

    /// <summary>Navigator sync after a document change (not recorded in history).</summary>
    private void SyncNavigator()
    {
        if (FramedNodeId is null)
        {
            _framedLeaves = ImmutableArray<string>.Empty;
            return;
        }

        var entries = LayoutSolver.FocusLayout(Document.Root);
        if (entries.TryGetValue(FramedNodeId, out var e))
        {
            _framedLeaves = LayoutEdits.LeafIds(e.Node);
            return;
        }

        var treeLeaves = new HashSet<string>(LayoutEdits.LeafIds(Document.Root), StringComparer.Ordinal);
        var kept = _framedLeaves.Where(treeLeaves.Contains).ToList();
        string? chosen = null;
        if (kept.Count > 0)
        {
            int best = int.MaxValue;
            foreach (var row in LayoutSolver.LayoutRects(Document.Root))
            {
                if (row.Value.Virtual) continue;
                var leaves = LayoutEdits.LeafIds(row.Value.Node);
                if (kept.All(k => leaves.Contains(k)) && leaves.Length <= best)
                {
                    best = leaves.Length;
                    chosen = row.Key;
                }
            }
        }

        if (chosen != FramedNodeId)
        {
            FramedNodeId = chosen;
            ClearZoomMemory();
        }

        RefreshFramedLeaves();
    }

    // ---------- focus and reveal state ----------

    /// <summary>Window the next landing should focus; cleared by FocusAfterLanding. Settable for tests.</summary>
    internal WindowKey? LandingFocus { get; set; }

    /// <summary>Key the engine last activated and whose focus echo has not arrived yet.</summary>
    internal WindowKey? EngineActivatedKey { get; private set; }

    /// <summary>Key whose reveal was requested while not at rest (stored only; #15 uses it).</summary>
    internal WindowKey? PendingReveal { get; private set; }

    /// <summary>Reveals are suppressed while Clock.Now is below this. Initial negative infinity.</summary>
    internal double SuppressRevealUntil { get; private set; } = double.NegativeInfinity;

    /// <summary>Reveals are suppressed while Clock.Now is below this. Initial negative infinity.</summary>
    internal double CloseQuietUntil { get; private set; } = double.NegativeInfinity;

    /// <summary>Time of the last zoom/un-zoom toggle from an app resize. Initial negative infinity.</summary>
    internal double LastZoomToggle { get; private set; } = double.NegativeInfinity;

    /// <summary>Last focus time of a managed window (negative infinity if never focused), or null if not managed.</summary>
    /// <param name="key">The window.</param>
    /// <returns>The time or null.</returns>
    internal double? LastFocusedTime(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _writer.TryGetValue(key, out var s) ? s.LastFocused : null;
    }

    /// <summary>Snap-back times of a managed window, oldest first (copy); empty if not managed.</summary>
    /// <param name="key">The window.</param>
    /// <returns>The times.</returns>
    internal IReadOnlyList<double> SnapBackTimes(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _writer.TryGetValue(key, out var s) ? s.SnapBackTimes.ToArray() : Array.Empty<double>();
    }

    /// <summary>lastFocused = now, FocusedKey = key, EngineActivatedKey = key, then
    /// Services.Windows.Focus(key). Unmanaged key: nothing.</summary>
    /// <param name="key">The window.</param>
    internal void Activate(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Run(() =>
        {
            if (!_writer.TryGetValue(key, out var s)) return;
            s.LastFocused = Services.Clock.Now;
            FocusedKey = key;
            EngineActivatedKey = key;
            Services.Windows.Focus(key);
        });
    }

    /// <summary>Focus the right candidate after a landing. Called by LandingDone; callable directly by tests.</summary>
    internal void FocusAfterLanding() => Run(() =>
    {
        var targets = ComputeTargets();
        var candidates = _windows.Select(w => w.Info.Key)
            .Where(k => targets.TryGetValue(k, out var t) && t.Kind == TargetKind.Tile).ToList();
        WindowKey? chosen = null;
        if (LandingFocus is { } lf && candidates.Contains(lf)) chosen = lf;
        else if (FocusedKey is { } fk && candidates.Contains(fk)) chosen = fk;
        else
        {
            double best = double.NegativeInfinity;
            foreach (var k in candidates)
            {
                double t = _writer[k].LastFocused;
                // SPEC-ASSUMPTION A12: a never-focused candidate is never chosen (H4); ties go to registration order.
                if (double.IsFinite(t) && (chosen is null || t > best))
                {
                    chosen = k;
                    best = t;
                }
            }
        }

        if (chosen is not null) Activate(chosen);
        SuppressRevealUntil = Services.Clock.Now + Tuning.LandingRevealQuietSeconds;
        LandingFocus = null;
    });

    /// <summary>The desktop-slot test for the current document and framing (H1).</summary>
    /// <returns>Whether the empty desktop slot is visible and large enough.</returns>
    internal bool DesktopSlotUsable()
    {
        bool result = false;
        Run(() => result = DesktopSlotUsableCore());
        return result;
    }

    private bool DesktopSlotUsableCore()
    {
        var d = LayoutTree.FindDesktop(Document.Root);
        if (d is null || d.Child is not null) return false;
        var entries = LayoutSolver.FocusLayout(Document.Root);
        if (!entries.TryGetValue(d.Id, out var e)) return false;
        Rect? framed = FramedNodeId is not null && entries.TryGetValue(FramedNodeId, out var fe) ? fe.Rect : null;
        var view = ProjectionView.AtRest(VisibleArea, Policy.Gap, framed);
        var f = ScreenProjection.DockedFrame(view, e.Rect);
        return ScreenProjection.OnStageFraction(f, view.Stage) >= ScreenProjection.OnStageThreshold
            && f.Width >= WindowEvents.DesktopSlotMinWidth
            && f.Height >= WindowEvents.DesktopSlotMinHeight;
    }

    // ---------- shared helpers ----------

    private PanelNode? TreePanelOf(WindowKey key) =>
        LayoutEdits.PanelsOf(Document.Root).FirstOrDefault(p => p.Views.Contains(key.Value, StringComparer.Ordinal));

    /// <summary>Window keys of a document: tree panels (pre-order), floating panels, hidden panels.</summary>
    private static List<string> WindowKeysOf(LayoutDocument doc)
    {
        var keys = new List<string>();
        foreach (var p in LayoutEdits.PanelsOf(doc.Root)) keys.AddRange(p.Views);
        foreach (var f in doc.Floating) keys.AddRange(f.Panel.Views);
        foreach (var h in doc.Hidden) keys.AddRange(h.Panel.Views);
        return keys;
    }

    /// <summary>Commit a layout edit: set the document, sync the navigator, then present.</summary>
    private void Commit(LayoutDocument doc, WindowKey? focus)
    {
        Document = doc;
        SyncNavigator();
        if (CanPresentDirectly)
        {
            PresentDirectly(focus);
            return;
        }

        Save0();
        Render();
        _changeRequested = true;
        if (focus is not null) LandingFocus = focus;
        PresentLayout(focus);
    }

    /// <summary>Remove a window's view from its panel, wherever the panel is.</summary>
    private LayoutDocument RemoveView(LayoutDocument doc, WindowKey key)
    {
        var p = LayoutEdits.FindPanelOfWindow(doc, key.Value);
        if (p is null) return doc;
        if (p.Views.Length == 1) return LayoutEdits.RemovePanel(doc, p.Id, Policy.ShareRemovedSpace);

        int idx = p.Views.IndexOf(key.Value);
        var views = p.Views.Where(v => v != key.Value).ToList();
        string? selected = p.Selected is { } sel && views.Contains(sel) ? sel : views[Math.Min(idx, views.Count - 1)];
        var replacement = new PanelNode(p.Id, views, selected, p.Tabbed);

        if (LayoutEdits.PanelsOf(doc.Root).Any(x => x.Id == p.Id))
            return doc with { Root = ReplacePanel(doc.Root, replacement) };
        if (doc.Floating.Any(f => f.Panel.Id == p.Id))
        {
            return new LayoutDocument(doc.Root,
                doc.Floating.Select(f => f.Panel.Id == p.Id ? f with { Panel = replacement } : f),
                doc.Hidden, doc.HiddenDesktop);
        }

        return new LayoutDocument(doc.Root, doc.Floating,
            doc.Hidden.Select(h => h.Panel.Id == p.Id ? h with { Panel = replacement } : h), doc.HiddenDesktop);
    }

    private static LayoutNode? ReplacePanel(LayoutNode? node, PanelNode replacement) => node switch
    {
        PanelNode p when p.Id == replacement.Id => replacement,
        SplitNode s => new SplitNode(s.Id, s.Axis, s.Weights, s.Children.Select(c => ReplacePanel(c, replacement)!)),
        DesktopNode { Child: { } c } d => d with { Child = ReplacePanel(c, replacement) },
        _ => node,
    };

    // ---------- Start ----------

    private void StartCore(IReadOnlyList<WindowInfo> windows)
    {
        foreach (var w in windows) Register(w);

        var cur = new HashSet<string>(_windows.Select(w => w.Info.Key.Value), StringComparer.Ordinal);
        var loaded = Services.Displays.Load(Key);
        LayoutDocument doc;
        string? framed;
        if (loaded is null || !WindowKeysOf(loaded.Document).Any(cur.Contains))
        {
            doc = new LayoutDocument(new DesktopNode());
            framed = null;
            // SPEC-ASSUMPTION A3: an ignored saved record also drops saved views, owners, dock spots and float sizes.
            _saved = new SavedDisplay(LayoutDocument.Empty);
        }
        else
        {
            doc = LayoutSanitizer.Sanitize(loaded.Document, cur.Contains);
            _saved = loaded;
            // SPEC-ASSUMPTION A4: a saved framed id is kept only when it names a FocusLayout entry.
            framed = loaded.FramedNodeId is { } fid && LayoutSolver.FocusLayout(doc.Root).ContainsKey(fid) ? fid : null;
        }

        // SPEC-ASSUMPTION A5/A6: the desktop is inserted on the Right with fraction 0.5, only at start.
        if (LayoutTree.FindDesktop(doc.Root) is null && doc.HiddenDesktop is null)
        {
            doc = doc.Root is null
                ? doc with { Root = new DesktopNode() }
                : doc with
                {
                    Root = LayoutEdits.InsertBeside(doc.Root, doc.Root.Id, new DesktopNode(), Edge.Right,
                        Ids.Make(IdPrefixes.Split), LayoutEdits.DefaultFraction),
                };
        }

        // SPEC-ASSUMPTION A7: windows that already have a panel (including hidden ones) keep it.
        // SPEC-ASSUMPTION A9: dropping docked windows moved while unmanaged (findings rule 4) is deferred.
        foreach (var m in _windows.OrderBy(w => w.Info.Key.Value, StringComparer.Ordinal).ToList())
        {
            var key = m.Info.Key;
            if (LayoutEdits.FindPanelOfWindow(doc, key.Value) is not null) continue;
            if (!m.Info.Dockable || doc.Root is null) continue; // SPEC-ASSUMPTION M22: float gives no panel.
            var o = Placement.PlaceNew(doc, key.Value, framed ?? doc.Root.Id, null, StageAspect, Ids,
                Policy.ShareRemovedSpace);
            if (o.Kind == PlacementKind.Docked) doc = o.Document;
            // else NeedsFloat: SPEC-ASSUMPTION M22 - no panel.
        }

        Document = doc;
        FramedNodeId = framed;
        _framingHistory.Clear();
        _framedLeaves = ImmutableArray<string>.Empty;
        ClearZoomMemory();
        SyncNavigator();

        IsStarted = true;
        Render();
        Save0();
        // SPEC-ASSUMPTION A2: Start lands synchronously, without the re-land delay.
        int g = ++PlacementGeneration;
        PlaceWindows(g);
    }

    // ---------- WindowAdded ----------

    private void AddedCore(WindowInfo info)
    {
        var key = info.Key;
        var panel = LayoutEdits.FindPanelOfWindow(Document, key.Value);
        var doc = Document;
        if (panel is not null)
        {
            bool hiddenOnly = !LayoutEdits.PanelsOf(Document.Root).Any(p => p.Id == panel.Id)
                && !Document.Floating.Any(f => f.Panel.Id == panel.Id);
            if (!hiddenOnly)
            {
                Save0();
                Render();
                return;
            }

            // SPEC-ASSUMPTION M19: a window found in the hidden list is taken out of it and placed as new.
            doc = RemoveView(Document, key);
        }

        // SPEC-ASSUMPTION M19: a remembered spot is ignored; the returning window is placed with PlaceNew.
        var current = Window(key)!.Info;
        LayoutDocument result = doc;
        if (Policy.FloatNewWindowsOnDesktop && DesktopSlotUsableCore())
        {
            // SPEC-ASSUMPTION M22: the desktop float gives the window no panel.
        }
        else if (current.Dockable)
        {
            string? framedId = FramedNodeId ?? Document.Root?.Id;
            if (framedId is not null)
            {
                var o = Placement.PlaceNew(doc, key.Value, framedId, FocusedKey?.Value, StageAspect, Ids,
                    Policy.ShareRemovedSpace);
                // SPEC-ASSUMPTION M22: NeedsFloat gives the window no panel.
                if (o.Kind == PlacementKind.Docked) result = o.Document;
            }
        }
        // else: SPEC-ASSUMPTION M22: a non-dockable window gets no panel.

        // SPEC-ASSUMPTION A10: the framing is not widened for the new window.
        if (result != Document)
        {
            Commit(result, key);
            return;
        }

        Save0();
        Render();
    }

    // ---------- WindowFocused ----------

    private void FocusedCore(WindowKey key, int index)
    {
        // SPEC-ASSUMPTION A11: the engine-activated marker is consumed by the next WindowFocused of any key.
        bool fromEngine = key == EngineActivatedKey;
        EngineActivatedKey = null;
        double now = Services.Clock.Now;
        _writer[key].LastFocused = now;
        FocusedKey = key;
        _lastFocused[_windows[index].Info.ProcessId] = key;
        if (IsStarted) RevealTest(key, fromEngine, now);
        Render();
    }

    private void RevealTest(WindowKey key, bool fromEngine, double now)
    {
        if (fromEngine || now < SuppressRevealUntil || now < CloseQuietUntil) return;
        if (!ComputeTargets().TryGetValue(key, out var t) || t.Kind == TargetKind.Tile) return;
        if (!CanPresentDirectly)
        {
            PendingReveal = key;
            return;
        }

        SuppressRevealUntil = now + WindowEvents.RevealQuietSeconds;
        string node = TreePanelOf(key)?.Id ?? DesktopNode.DefaultId;
        LandingFocus = key;
        FrameNode(node, true);
        ReLand();
        LogEvent($"reveal {key.Value}");
    }

    // ---------- WindowMoved ----------

    private void MovedCore(WindowKey key, int index, Rect frame)
    {
        Rect prev = _windows[index].Info.Frame;
        _windows[index] = _windows[index] with { Info = _windows[index].Info with { Frame = frame } };
        if (!IsStarted) return;

        var s = _writer[key];
        if (s.WritesInFlight > 0 || Services.Windows.MouseDown || IsExpected(key, frame)) return;

        if (_windows[index].Parked)
        {
            SetParked(key, false);
            WriteTargets(ComputeTargets());
            Render();
            return;
        }

        var panel = TreePanelOf(key);
        if (panel is null) return;

        double now = Services.Clock.Now;
        var targets = ComputeTargets();
        Rect tile = targets.TryGetValue(key, out var tg) && tg.Kind == TargetKind.Tile ? tg.Rect : frame;
        Rect va = VisibleArea;
        bool resized = Math.Abs(frame.Width - prev.Width) > WindowEvents.ResizedThreshold
            || Math.Abs(frame.Height - prev.Height) > WindowEvents.ResizedThreshold;
        bool grew = (frame.Width > tile.Width + WindowEvents.GrowWidthMargin
                     && frame.Height >= tile.Height - WindowEvents.GrowKeepSlack)
            || (frame.Height > tile.Height + WindowEvents.GrowHeightMargin
                && frame.Width >= tile.Width - WindowEvents.GrowKeepSlack);
        bool shrank = frame.Width < tile.Width - WindowEvents.ShrinkMargin
            || frame.Height < tile.Height - WindowEvents.ShrinkMargin;
        bool fills = frame.Width >= WindowEvents.FillsWidthFraction * va.Width
            && frame.Height >= WindowEvents.FillsHeightFraction * va.Height;
        bool drift = Math.Abs(frame.X - tile.X) > WindowEvents.UnzoomDrift
            || Math.Abs(frame.Y - tile.Y) > WindowEvents.UnzoomDrift
            || Math.Abs(frame.Width - tile.Width) > WindowEvents.UnzoomDrift
            || Math.Abs(frame.Height - tile.Height) > WindowEvents.UnzoomDrift;
        bool alone = FramedNodeId == panel.Id;

        if (resized && now - LastZoomToggle > WindowEvents.ZoomToggleSpacingSeconds)
        {
            if ((alone && shrank) || (alone && fills && drift))
            {
                // un-zoom
                LastZoomToggle = now;
                string? back = ZoomedAlone ? ZoomReturnNodeId : null;
                FrameNode(back, true);
                ReLand();
                LogEvent($"un-zoom {key.Value}");
                return;
            }

            if (!alone && grew)
            {
                // zoom
                LastZoomToggle = now;
                string? r = FramedNodeId;
                FrameNode(panel.Id, true);
                ZoomedAlone = true;
                ZoomReturnNodeId = r;
                ReLand();
                LogEvent($"zoom {key.Value}");
                return;
            }
        }

        SnapBack(key, s, frame, tile, va, now);
    }

    private void SnapBack(WindowKey key, WriterRecord s, Rect frame, Rect tile, Rect va, double now)
    {
        var l = s.SnapBackTimes;
        l.RemoveAll(t => now - t >= Tuning.SnapBackWindowSeconds);
        l.Add(now);
        if (l.Count >= Tuning.SnapBackLimit)
        {
            l.Clear();
            if (frame.Width <= tile.Width + WindowEvents.InsistSizeSlack
                && frame.Height <= tile.Height + WindowEvents.InsistSizeSlack)
            {
                s.Maximum = new Size(frame.Width, frame.Height);
            }
            else
            {
                bool qw = frame.Width > tile.Width + WindowEvents.InsistSizeSlack
                    && frame.Width < WindowEvents.InsistScreenFactor * va.Width;
                bool qh = frame.Height > tile.Height + WindowEvents.InsistSizeSlack
                    && frame.Height < WindowEvents.InsistScreenFactor * va.Height;
                if (qw || qh)
                {
                    double ow = s.Minimum?.Width ?? 0, oh = s.Minimum?.Height ?? 0;
                    s.Minimum = new Size(qw ? Math.Max(ow, frame.Width) : ow, qh ? Math.Max(oh, frame.Height) : oh);
                    Services.Limits.Learn(Window(key)!.Info.App, s.Minimum.Value);
                }
            }

            LogEvent($"app size accepted for {key.Value}");
        }

        WriteTargets(ComputeTargets());
        Render();
    }

    // ---------- Move and ArrangeAll ----------

    private void MoveCore(Edge toward)
    {
        if (!IsStarted || IsSuspended) return;
        var entries = LayoutSolver.FocusLayout(Document.Root);
        string? current = null;
        if (FramedNodeId is not null && entries.ContainsKey(FramedNodeId)) current = FramedNodeId;
        // SPEC-ASSUMPTION H2: with nothing framed, start from the focused window's tree panel.
        else if (FocusedKey is { } fk) current = TreePanelOf(fk)?.Id;
        if (current is null) return;

        string? n = DirectionalFocus.Neighbour(entries, current, toward);
        if (n is null) return;
        FrameNode(n, true);
        ReLand();
    }

    private void ArrangeAllCore()
    {
        if (!IsStarted || IsSuspended) return;
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in Services.Windows.FrontToBack())
        {
            var m = Window(k);
            if (m is null || !m.Info.Dockable) continue;
            var p = LayoutEdits.FindPanelOfWindow(Document, k.Value);
            if (p is null || Document.Hidden.Any(h => h.Panel.Id == p.Id)) continue;
            if (seen.Add(k.Value)) order.Add(k.Value);
        }

        foreach (var v in WindowKeysOf(Document))
        {
            if (seen.Contains(v)) continue;
            var m = Window(new WindowKey(v));
            if (m is null || m.Info.Dockable)
            {
                seen.Add(v);
                order.Add(v);
            }
        }

        bool landscape = Stage.Width >= Stage.Height;
        var doc = Placement.ArrangeDwindle(Document, order, landscape, Ids);
        FrameNode(null, Phase != StagePhase.Overview);
        Commit(doc, null);
    }
}
