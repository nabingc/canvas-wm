using CanvasWm.Core.Layout;

namespace CanvasWm.Core.Tests.DockingSpec;

/// <summary>Wrappers that call the product and assert inputs are unchanged (AC40).</summary>
internal static class Ops
{
    public static LayoutNode? Rm(LayoutNode? root, string id, bool? share = null)
    {
        var snap = root is null ? null : T.Clone(root);
        var r = share is null ? LayoutEdits.RemoveNode(root, id) : LayoutEdits.RemoveNode(root, id, share.Value);
        Assert.Equal(snap, root);
        return r;
    }

    public static LayoutNode? Ins(LayoutNode? root, string target, LayoutNode n, Edge e, string sid = "n1", double f = 0.5)
    {
        var snap = root is null ? null : T.Clone(root);
        var nsnap = T.Clone(n);
        var r = LayoutEdits.InsertBeside(root, target, n, e, sid, f);
        Assert.Equal(snap, root);
        Assert.Equal(nsnap, n);
        return r;
    }

    public static LayoutNode? Seam(LayoutNode? root, SplitSeam seam, LayoutNode n, string sid = "n1")
    {
        var snap = root is null ? null : T.Clone(root);
        var r = LayoutEdits.InsertAtSeam(root, seam, n, sid);
        Assert.Equal(snap, root);
        return r;
    }

    public static LayoutDocument Dock(LayoutDocument doc, string key, DockTargetSpec t, SequenceIdGenerator g, bool? share = null)
    {
        var snap = T.CloneDoc(doc);
        var r = share is null ? Docking.Dock(doc, key, t, g) : Docking.Dock(doc, key, t, g, share.Value);
        Assert.Equal(snap, doc);
        return r;
    }

    public static LayoutDocument Unit(LayoutDocument doc, PanelNode p, DockTargetSpec t, SequenceIdGenerator g)
    {
        var snap = T.CloneDoc(doc);
        var r = Docking.DockUnit(doc, p, t, g);
        Assert.Equal(snap, doc);
        return r;
    }

    public static DockTargetSpec Beside(string id, Edge e) => new(id, e);
}
