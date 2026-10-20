using System.Collections.Immutable;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;

namespace CanvasWm.Core.Projection;

/// <summary>The projection pass.</summary>
public static class ProjectionTargets
{
    /// <summary>Computes placements and targets.</summary>
    /// <param name="request">The request.</param>
    /// <exception cref="ArgumentNullException">request is null.</exception>
    public static ProjectionResult Compute(ProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        LayoutDocument doc = request.Document;
        ProjectionView view = request.View;
        // SPEC-ASSUMPTION A15: entries are weights-mode (range entries are harmless).
        LayoutEntries entries = request.Entries ?? LayoutSolver.LayoutRects(doc.Root);

        var placements = ImmutableArray.CreateBuilder<PanelPlacement>();
        var targets = new Dictionary<string, WindowTarget>(StringComparer.Ordinal);

        void Add(PanelNode panel, Rect frame, bool floating)
        {
            string key = panel.Selected!;
            request.Windows.TryGetValue(key, out WindowFacts? facts);
            // SPEC-ASSUMPTION A10: floats use the tile minimum rule.
            Size m = ScreenProjection.EffectiveMinimum(request.Policy.FixedMinimum, facts?.Minimum, request.Tuning);
            if (floating)
            {
                if (facts?.Natural is { } nat)
                {
                    frame = ScreenProjection.FitAspect(frame, nat);
                }
                if (!request.CollapsedPanels.Contains(panel.Id))
                {
                    frame = ScreenProjection.GrowToMinimum(frame, m, view.Stage);
                }
            }
            // SPEC-ASSUMPTION A8: collapsed panels are not on stage, so they park without a picture flag.
            bool collapsed = request.CollapsedPanels.Contains(panel.Id);
            double fraction = ScreenProjection.OnStageFraction(frame, view.Stage);
            bool onStage = !collapsed
                && (fraction >= ScreenProjection.OnStageThreshold
                    || (floating && request.DesktopSlotUsable
                        && fraction > ScreenProjection.FloatingOnStageThreshold));
            bool fits = ScreenProjection.Fits(frame, m);

            Rect? strip = null;
            double stripHeight = 0;
            if (panel.HasStrip)
            {
                stripHeight = ScreenProjection.StripFullHeight(request.Policy.TabPadding, request.Tuning);
                // SPEC-ASSUMPTION A13: strip is reported as data only; the window frame is not reduced.
                strip = ScreenProjection.StripRect(frame, stripHeight, request.Policy.TabInset);
            }
            placements.Add(new PanelPlacement(panel.Id, key, frame, floating, onStage, fits, strip, stripHeight));

            // SPEC-ASSUMPTION A7: duplicate window keys: the first placement wins the target.
            if (facts is null || targets.ContainsKey(key))
            {
                return;
            }
            targets[key] = MakeTarget(request, frame, onStage, fits, m, facts);
        }

        void Walk(LayoutNode? node)
        {
            switch (node)
            {
                case SplitNode s:
                    foreach (LayoutNode child in s.Children)
                    {
                        Walk(child);
                    }
                    break;
                case DesktopNode d:
                    Walk(d.Child);
                    break;
                case PanelNode p:
                    // SPEC-ASSUMPTION A6: a panel with null or foreign Selected gets no placement.
                    if (p.Selected is not null && p.Views.Contains(p.Selected)
                        && entries.TryGetValue(p.Id, out LayoutEntry? e))
                    {
                        Add(p, ScreenProjection.DockedFrame(view, e.Rect), false);
                    }
                    break;
            }
        }

        Walk(doc.Root);
        foreach (FloatingPanel fp in doc.Floating)
        {
            PanelNode p = fp.Panel;
            if (p.Selected is not null && p.Views.Contains(p.Selected)
                && request.FloatingUnitRects.TryGetValue(p.Id, out Rect unit))
            {
                Add(p, ScreenProjection.Project(view, unit), true);
            }
        }
        return new ProjectionResult(placements.ToImmutable(), targets);
    }

    private static WindowTarget MakeTarget(ProjectionRequest request, Rect frame, bool onStage, bool fits,
                                           Size m, WindowFacts facts)
    {
        if (onStage && fits)
        {
            return new WindowTarget(TargetKind.Tile,
                ScreenProjection.RoundEdges(ScreenProjection.ClampToMaximum(frame, facts.Maximum)));
        }
        if (!onStage)
        {
            // SPEC-ASSUMPTION A5: unknown last frame size falls back to 800 x 600.
            Size z = facts.LastFrameSize ?? ScreenProjection.FallbackWindowSize;
            Rect park = request.Policy.ParkOutOfViewSmall
                ? ParkGeometry.ParkSmallFrame(request.ParkDisplay, z, request.ParkOptions)
                : ParkGeometry.ParkFrame(request.ParkDisplay, z, null, request.ParkOptions);
            return new WindowTarget(TargetKind.Park, park);
        }
        // SPEC-ASSUMPTION A4: the picture park frame passes the effective minimum to park geometry.
        Size preview = ScreenProjection.PreviewSize(frame, m, facts.LastFrameSize);
        return new WindowTarget(TargetKind.ParkWithPicture,
            ParkGeometry.ParkFrame(request.ParkDisplay, preview, m, request.ParkOptions));
    }
}
