using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.DockingSpec.T;

namespace CanvasWm.Core.Tests.DockingSpec;

public class DockingPropertyTests
{
    private int _ctr;
    private bool _desktopUsed;

    private LayoutNode GenNode(Random r, int depth, Axis? parent)
    {
        if (depth <= 0 || r.NextDouble() < 0.3)
        {
            if (!_desktopUsed && r.NextDouble() < 0.08)
            {
                _desktopUsed = true;
                return D();
            }
            return GenPanel(r);
        }
        var axis = parent is null ? (r.Next(2) == 0 ? Axis.X : Axis.Y) : (parent == Axis.X ? Axis.Y : Axis.X);
        var n = r.Next(2, 5);
        var kids = Enumerable.Range(0, n).Select(_ => GenNode(r, depth - 1, axis)).ToList();
        var ws = kids.Select(_ => r.NextDouble() + 0.1).ToList();
        var sum = ws.Sum();
        return new SplitNode("s" + _ctr++, axis, ws.Select(w => w / sum), kids);
    }

    private PanelNode GenPanel(Random r)
    {
        var id = "p" + _ctr++;
        var views = Enumerable.Range(0, r.Next(1, 4)).Select(_ => "k" + _ctr++).ToList();
        return new PanelNode(id, views, views[0], r.Next(2) == 0);
    }

    private LayoutDocument GenDoc(Random r)
    {
        _desktopUsed = false;
        var tree = GenNode(r, 4, null);
        LayoutNode? root = tree;
        if (!_desktopUsed && r.NextDouble() < 0.3)
        {
            _desktopUsed = true;
            root = D(tree);
        }
        var fl = Enumerable.Range(0, r.Next(3)).Select(_ => Fl(GenPanel(r))).ToList();
        var hi = Enumerable.Range(0, r.Next(3)).Select(_ => Hd(GenPanel(r))).ToList();
        return new LayoutDocument(root, fl, hi);
    }

    private static void CheckNormalised(LayoutNode? n, string log)
    {
        switch (n)
        {
            case null: return;
            case PanelNode p:
                Assert.True(p.Views.Length > 0, "empty panel " + log);
                return;
            case DesktopNode d:
                CheckNormalised(d.Child, log);
                return;
            case SplitNode s:
                Assert.True(s.Children.Length >= 2, "split <2 children " + s.Id + " " + log);
                Assert.True(s.Weights.Length == s.Children.Length, "weights length " + log);
                Assert.True(s.Weights.All(w => w > 0 && double.IsFinite(w)), "bad weight " + log);
                Assert.True(Math.Abs(s.Weights.Sum() - 1) < 1e-9, "sum " + s.Weights.Sum() + " " + log);
                foreach (var c in s.Children)
                {
                    Assert.False(c is SplitNode cs && cs.Axis == s.Axis, "same-axis child " + log);
                    CheckNormalised(c, log);
                }
                return;
        }
    }

    private static List<string> PanelIds(LayoutNode? n) => LayoutEdits.PanelsOf(n).Select(p => p.Id).ToList();

    private static List<string> AllKeys(LayoutDocument d) =>
        LayoutEdits.PanelsOf(d.Root).SelectMany(p => p.Views)
            .Concat(d.Floating.SelectMany(f => f.Panel.Views))
            .Concat(d.Hidden.SelectMany(h => h.Panel.Views)).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static List<string> AllPanelIds(LayoutDocument d) =>
        PanelIds(d.Root).Concat(d.Floating.Select(f => f.Panel.Id)).Concat(d.Hidden.Select(h => h.Panel.Id))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static List<SplitNode> Splits(LayoutNode? n) => Walk(n).OfType<SplitNode>().ToList();

    [Fact]
    public void AC41_RandomOperationSequencesKeepInvariants()
    {
        var r = new Random(20240607);
        var changed = 0;
        for (var docIdx = 0; docIdx < 600; docIdx++)
        {
            var doc = GenDoc(r);
            var gen = new SequenceIdGenerator();
            var stale = new List<DockTargetSpec>();
            var log = new List<string>();
            var opCount = r.Next(1, 11);
            for (var op = 0; op < opCount; op++)
            {
                var ctx = () => $"[doc {docIdx} op {op}: {string.Join(" | ", log)}] tree={Dump(doc.Root)}";
                var root = doc.Root;
                var before = Walk(root).ToList();
                var ids = before.Select(n => n.Id).ToList();
                var desktopBefore = before.OfType<DesktopNode>().FirstOrDefault();
                var panelsBefore = PanelIds(root);
                var inputDoc = doc;
                var snapDoc = CloneDoc(doc);
                var kind = r.Next(4);
                var newPanel = PanelNode.ForWindow("new" + _ctr++);
                bool desktopMayVanish = false;

                if (kind == 0)
                {
                    var id = r.Next(5) == 0 || ids.Count == 0 ? "absent" : ids[r.Next(ids.Count)];
                    var share = r.Next(2) == 0;
                    log.Add($"Remove {id} {share}");
                    var removed = before.FirstOrDefault(n => n.Id == id);
                    var res = LayoutEdits.RemoveNode(root, id, share);
                    CheckNormalised(res, ctx());
                    var gone = removed is DesktopNode ? new List<string>() : PanelIds(removed);
                    var expected = panelsBefore.Except(gone).ToList();
                    var after = PanelIds(res);
                    Assert.True(expected.OrderBy(x => x).SequenceEqual(after.OrderBy(x => x)), "remove panels " + ctx());
                    desktopMayVanish = removed is not null && removed is not DesktopNode && Walk(removed).Any(n => n is DesktopNode);
                    doc = doc with { Root = res };
                    AssertDesktop(desktopBefore, res, desktopMayVanish, ctx());
                }
                else if (kind == 1)
                {
                    var id = r.Next(5) == 0 || ids.Count == 0 ? "absent" : ids[r.Next(ids.Count)];
                    var edge = (Edge)r.Next(4);
                    var f = r.NextDouble() * 2 - 0.5;
                    log.Add($"Insert {id} {edge} {f}");
                    var res = LayoutEdits.InsertBeside(root, id, newPanel, edge, "q" + _ctr++, f);
                    CheckInsert(root, res, newPanel, panelsBefore, ctx());
                    doc = doc with { Root = res };
                    AssertDesktop(desktopBefore, res, false, ctx());
                }
                else if (kind == 2)
                {
                    var seam = PickSeam(r, root, stale);
                    if (seam is null) { continue; }
                    log.Add($"Seam {seam.Seam}");
                    var res = LayoutEdits.InsertAtSeam(root, seam.Seam!, newPanel, "q" + _ctr++);
                    CheckInsert(root, res, newPanel, panelsBefore, ctx());
                    doc = doc with { Root = res };
                    AssertDesktop(desktopBefore, res, false, ctx());
                }
                else
                {
                    var allPanels = LayoutEdits.PanelsOf(root).Concat(doc.Floating.Select(f => f.Panel)).Concat(doc.Hidden.Select(h => h.Panel)).ToList();
                    var fresh = allPanels.Count == 0 || r.Next(4) == 0;
                    var key = "fresh" + _ctr++;
                    if (!fresh)
                    {
                        var pp = allPanels[r.Next(allPanels.Count)];
                        key = pp.Views[r.Next(pp.Views.Length)];
                    }
                    DockTargetSpec target;
                    var seamPick = r.Next(2) == 0 ? PickSeam(r, root, stale) : null;
                    if (seamPick is not null) { target = seamPick; }
                    else
                    {
                        var cand = new List<string>(ids) { "absent" };
                        cand.AddRange(allPanels.Select(p => p.Id));
                        target = new DockTargetSpec(cand[r.Next(cand.Count)], (Edge)r.Next(4));
                    }
                    var share = r.Next(2) == 0;
                    log.Add($"Dock {key} fresh={fresh} -> {target.Id} {target.Edge} seam={target.Seam} share={share}");
                    var keysBefore = AllKeys(doc);
                    var idsBefore = AllPanelIds(doc);
                    var moved = LayoutEdits.FindPanelOfWindow(doc, key);
                    Assert.Equal(fresh, moved is null);
                    var callsBefore = gen.Calls;
                    var res = Docking.Dock(doc, key, target, gen, share);
                    var used = gen.Calls - callsBefore;
                    Assert.True(used is 1 or 2, "calls " + used + ctx());
                    CheckNormalised(res.Root, ctx());
                    var expKeys = fresh ? keysBefore.Append(key).OrderBy(x => x, StringComparer.Ordinal).ToList() : keysBefore;
                    Assert.True(expKeys.SequenceEqual(AllKeys(res)), "keys " + ctx());
                    var expIds = fresh ? idsBefore.Append(key).OrderBy(x => x, StringComparer.Ordinal).ToList() : idsBefore;
                    Assert.True(expIds.SequenceEqual(AllPanelIds(res)), "panel ids " + ctx());
                    var movedId = fresh ? key : moved!.Id;
                    var inTree = LayoutEdits.PanelsOf(res.Root).Where(p => p.Id == movedId).ToList();
                    Assert.True(inTree.Count == 1, "moved count " + ctx());
                    Assert.Equal(fresh ? PanelNode.ForWindow(key) : moved!, inTree[0]);
                    AssertDesktop(desktopBefore, res.Root, false, ctx());
                    doc = res;
                }
                // (f) the input document is structurally unchanged after the op
                Assert.True(snapDoc.Equals(inputDoc), "input mutated " + ctx());
                changed += ReferenceEquals(inputDoc.Root, doc.Root) ? 0 : 1;
            }
        }
        Assert.True(changed > 600, "property test too trivial: " + changed);
    }

    private static void AssertDesktop(DesktopNode? before, LayoutNode? after, bool mayVanish, string ctx)
    {
        if (before is null || mayVanish) return;
        Assert.True(Walk(after).Any(n => n is DesktopNode d && d.Id == before.Id), "desktop lost " + ctx);
    }

    private static void CheckInsert(LayoutNode? before, LayoutNode? res, PanelNode np, List<string> panelsBefore, string ctx)
    {
        CheckNormalised(res, ctx);
        if (ReferenceEquals(before, res)) return;
        var after = PanelIds(res);
        Assert.True(after.Count(i => i == np.Id) == 1, "new panel count " + ctx);
        foreach (var p in panelsBefore)
        {
            Assert.True(after.Count(i => i == p) == 1, "previous panel " + p + " " + ctx);
        }
    }

    private static DockTargetSpec? PickSeam(Random r, LayoutNode? root, List<DockTargetSpec> stale)
    {
        if (stale.Count > 0 && r.Next(3) == 0) return stale[r.Next(stale.Count)];
        var splits = Splits(root);
        if (splits.Count == 0) return null;
        var s = splits[r.Next(splits.Count)];
        var t = LayoutEdits.SeamTarget(s, r.Next(-1, s.Children.Length + 2));
        stale.Add(t);
        return t;
    }
}
