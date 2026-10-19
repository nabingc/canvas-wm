namespace CanvasWm.Core.Layout;

/// <summary>Moves panels into a document's tree.</summary>
public static class Docking
{
    /// <summary>Moves the panel holding windowKey (or a new one-window panel) to target.
    /// Never fails. Consumes one id from ids, two when it falls back.</summary>
    /// <param name="document">The document.</param>
    /// <param name="windowKey">Window to dock.</param>
    /// <param name="target">Where to dock.</param>
    /// <param name="ids">Id generator.</param>
    /// <param name="shareRemovedSpace">Variant of the removal of the old position.</param>
    /// <exception cref="ArgumentNullException">A non-nullable argument is null.</exception>
    public static LayoutDocument Dock(LayoutDocument document, string windowKey,
                                      DockTargetSpec target, IIdGenerator ids,
                                      bool shareRemovedSpace = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(windowKey);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ids);

        var panel = LayoutEdits.FindPanelOfWindow(document, windowKey);
        bool fresh = panel is null;
        panel ??= PanelNode.ForWindow(windowKey);

        // SPEC-ASSUMPTION A6: a fresh panel skips removal (the study removes by the window key).
        var doc1 = fresh ? document : LayoutEdits.RemovePanel(document, panel.Id, shareRemovedSpace);
        return DockUnit(doc1, panel, target, ids);
    }

    /// <summary>Inserts a detached panel at target without looking it up or removing it.</summary>
    /// <param name="document">The document.</param>
    /// <param name="panel">The panel to insert.</param>
    /// <param name="target">Where to dock.</param>
    /// <param name="ids">Id generator.</param>
    /// <exception cref="ArgumentNullException">A non-nullable argument is null.</exception>
    public static LayoutDocument DockUnit(LayoutDocument document, PanelNode panel,
                                          DockTargetSpec target, IIdGenerator ids)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ids);

        string id1 = ids.Make(IdPrefixes.Split);
        var root = document.Root;
        LayoutNode? newRoot;
        if (root is null)
        {
            newRoot = panel;
        }
        else if (target.Seam is not null)
        {
            newRoot = LayoutEdits.InsertAtSeam(root, target.Seam, panel, id1);
        }
        else
        {
            newRoot = LayoutEdits.InsertBeside(root, target.Id, panel, target.Edge, id1,
                LayoutEdits.DefaultFraction);
        }

        bool present = false;
        foreach (var p in LayoutEdits.PanelsOf(newRoot))
        {
            if (p.Id == panel.Id)
            {
                present = true;
                break;
            }
        }
        if (!present)
        {
            string id2 = ids.Make(IdPrefixes.Split);
            var fallback = target.Seam is { Axis: Axis.Y } ? Edge.Bottom : Edge.Right;
            newRoot = newRoot is null
                ? panel
                : LayoutEdits.InsertBeside(newRoot, newRoot.Id, panel, fallback, id2,
                    LayoutEdits.DefaultFraction);
        }
        return document with { Root = newRoot };
    }
}
