using CanvasWm.Core.Frames;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Parking;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;

namespace CanvasWm.Win32.Windows;

/// <summary>A prepared write entry. Identity null = not resolvable.</summary>
internal readonly record struct WriteRequest(WindowKey Key, ManagedWindowIdentity? Identity, Rect Target);

/// <summary>Runs one batch on the calling (writer) thread: B6-B15. Never throws. Keeps no state
/// between batches (B15).</summary>
internal sealed class FrameWriteRunner
{
    private readonly INativeWindowOps _ops;

    internal FrameWriteRunner(INativeWindowOps ops)
    {
        _ops = ops;
    }

    private sealed class Entry
    {
        internal nint H;
        internal Rect Target;
        internal int M;
        internal PixelRect Outer;
        internal bool OffScreen;
        internal bool Active;
        internal Rect? Result;
    }

    /// <summary>Runs a batch; one result per entry, in input order.</summary>
    internal IReadOnlyList<Rect?> Run(IReadOnlyList<WriteRequest> batch)
    {
        var results = new Rect?[batch.Count];
        var entries = new List<Entry>();
        try
        {
            // B5: one snapshot for the whole batch.
            IReadOnlyList<MonitorInfo> monitors = _ops.Monitors();
            if (monitors.Count == 0)
            {
                return results;
            }

            var dipFrames = new Rect[monitors.Count];
            for (int i = 0; i < dipFrames.Length; i++)
            {
                dipFrames[i] = monitors[i].DipFrame;
            }

            var byIndex = new Entry?[batch.Count];

            // B6-B8: eligibility, restore, outer rect.
            for (int i = 0; i < batch.Count; i++)
            {
                try
                {
                    Entry? e = Prepare(batch[i], monitors, dipFrames);
                    byIndex[i] = e;
                    if (e is not null)
                    {
                        entries.Add(e);
                    }
                }
                catch (Exception)
                {
                    // B13: this entry is null.
                }
            }

            WriteAll(entries);

            // B11: read-back, settle, re-push.
            foreach (Entry e in entries)
            {
                if (!e.Active)
                {
                    continue;
                }

                try
                {
                    e.Result = ReadBack(e, monitors, dipFrames);
                }
                catch (Exception)
                {
                    e.Result = null;
                }
            }

            for (int i = 0; i < byIndex.Length; i++)
            {
                results[i] = byIndex[i]?.Result;
            }
        }
        catch (Exception)
        {
            // B13: every not-yet-finished entry is null.
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = null;
            }
        }

        return results;
    }

    /// <summary>B16: current frame in DIPs, or null.</summary>
    internal Rect? Read(ManagedWindowIdentity? identity)
    {
        try
        {
            if (identity is not { } id)
            {
                return null;
            }

            nint h = HandleConversion.ToHwnd(id.Handle);
            return _ops.IsLive(h, id.ProcessId, id.ThreadId) ? _ops.ReadDipFrame(h) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>B17: true/false = SetForegroundWindow outcome; null = not attempted.</summary>
    /// <param name="identity">The window.</param>
    /// <param name="onHungSkipped">Called when the window is skipped because it is hung.</param>
    internal bool? Focus(ManagedWindowIdentity identity, Action? onHungSkipped = null)
    {
        try
        {
            nint h = HandleConversion.ToHwnd(identity.Handle);
            if (!_ops.IsLive(h, identity.ProcessId, identity.ThreadId))
            {
                return null;
            }

            if (_ops.IsHung(h))
            {
                onHungSkipped?.Invoke();
                return null;
            }

            if (_ops.IsMinimized(h))
            {
                _ops.Restore(h);
            }

            return _ops.SetForeground(h);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Entry? Prepare(WriteRequest req, IReadOnlyList<MonitorInfo> monitors, Rect[] dipFrames)
    {
        if (req.Identity is not { } id)
        {
            return null;
        }

        Rect t = req.Target;
        if (!double.IsFinite(t.X) || !double.IsFinite(t.Y) || !double.IsFinite(t.Width) || !double.IsFinite(t.Height))
        {
            return null;
        }

        nint h = HandleConversion.ToHwnd(id.Handle);
        if (!_ops.IsLive(h, id.ProcessId, id.ThreadId) || _ops.IsHung(h) || _ops.IsMinimized(h))
        {
            return null;
        }

        // B7: a maximised window is restored without activation before anything is measured.
        if (_ops.IsMaximized(h))
        {
            _ops.RestoreNoActivate(h);
        }

        int cur = _ops.MonitorIndexOf(h, monitors);
        int m = DisplayChoice.ForRect(t, dipFrames, cur);
        if (m < 0)
        {
            return null;
        }

        return new Entry
        {
            H = h,
            Target = t,
            M = m,
            Outer = ComputeOuter(h, t, cur, m, monitors),
            OffScreen = FrameSettle.IsOffScreenWrite(ParkGeometry.VisibleFraction(t, dipFrames)),
            Active = true,
        };
    }

    /// <summary>B8.3-B8.6 for a window now on monitor index cur, targeting monitor index m.</summary>
    private PixelRect ComputeOuter(nint h, Rect target, int cur, int m, IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo dest = monitors[m];
        PixelRect vis = DipSpace.ToPixels(target, dest.Bounds, dest.Dpi);

        FrameInsets insets = FrameInsets.Zero;
        PixelRect? outerNow = _ops.WindowRect(h);
        PixelRect? visibleNow = _ops.VisibleRect(h);
        if (outerNow is { } o && visibleNow is { } v)
        {
            insets = FrameInsets.Measure(o.Left, o.Top, o.Right, o.Bottom, v.Left, v.Top, v.Right, v.Bottom);
        }

        if (cur >= 0 && monitors[cur].Dpi != dest.Dpi)
        {
            insets = insets.Rescale(monitors[cur].Dpi, dest.Dpi);
        }

        return FrameMath.ClampOrigin(FrameMath.Expand(vis, insets));
    }

    private void WriteAll(List<Entry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        if (entries.Count == 1)
        {
            WriteOne(entries[0]);
            return;
        }

        bool deferred;
        try
        {
            var batch = new List<(nint Hwnd, PixelRect Outer)>(entries.Count);
            foreach (Entry e in entries)
            {
                batch.Add((e.H, e.Outer));
            }

            deferred = _ops.TryDeferBatch(batch);
        }
        catch (Exception)
        {
            // B13: an exception in the batch call leaves every entry null.
            foreach (Entry e in entries)
            {
                e.Active = false;
            }

            return;
        }

        if (deferred)
        {
            return;
        }

        foreach (Entry e in entries)
        {
            WriteOne(e);
        }
    }

    private void WriteOne(Entry e)
    {
        try
        {
            e.Active = _ops.SetWindowPos(e.H, e.Outer, positionOnly: false);
        }
        catch (Exception)
        {
            e.Active = false;
        }
    }

    private Rect? ReadBack(Entry e, IReadOnlyList<MonitorInfo> monitors, Rect[] dipFrames)
    {
        if (_ops.ReadDipFrame(e.H) is not { } r)
        {
            return null;
        }

        // Settle (B11.2).
        if (FrameSettle.NeedsSecondPass(e.Target, r))
        {
            int cur = _ops.MonitorIndexOf(e.H, monitors);
            PixelRect outer2 = ComputeOuter(e.H, e.Target, cur, e.M, monitors);
            if (_ops.SetWindowPos(e.H, outer2, positionOnly: false))
            {
                r = _ops.ReadDipFrame(e.H) ?? r;
            }

            e.Outer = outer2;
        }

        // Re-push (B11.3).
        if (FrameSettle.NeedsRepush(e.OffScreen, ParkGeometry.VisibleFraction(r, dipFrames)))
        {
            if (_ops.SetWindowPos(e.H, e.Outer, positionOnly: true))
            {
                r = _ops.ReadDipFrame(e.H) ?? r;
            }
        }

        return r;
    }
}
