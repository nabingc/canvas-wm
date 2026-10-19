using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Tests.DockingSpec;
using static CanvasWm.Core.Tests.DockingSpec.T;

namespace CanvasWm.Core.Tests.PlacementSpec;

public class PlacementPropertyTests
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
        var fl = Enumerable.Range(0, r.Next(4))
            .Select(_ => new FloatingPanel(GenPanel(r), new Rect(r.Next(5), r.Next(5), 1, 1), r.Next(-5, 11))).ToList();
        var hi = Enumerable.Range(0, r.Next(3)).Select(_ => Hd(GenPanel(r))).ToList();
        HiddenDesktop? hd = r.Next(2) == 0 ? null : new HiddenDesktop("g", Array.Empty<FloatingPanel>(), null, new[] { "x" }, 0.4);
        return new LayoutDocument(root, fl, hi, hd);
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

    private static List<string> Keys(LayoutNode? root, IEnumerable<FloatingPanel> fl, IEnumerable<HiddenPanel>? hi) =>
        LayoutEdits.PanelsOf(root).SelectMany(p => p.Views)
            .Concat(fl.SelectMany(f => f.Panel.Views))
            .Concat(hi is null ? Enumerable.Empty<string>() : hi.SelectMany(h => h.Panel.Views))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static List<string> PanelIds(LayoutDocument d) =>
        LayoutEdits.PanelsOf(d.Root).Select(p => p.Id)
            .Concat(d.Floating.Select(f => f.Panel.Id)).Concat(d.Hidden.Select(h => h.Panel.Id))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

    [Fact]
    public void AC29_PlaceNew_Property()
    {
        var r = new Random(4242);
        for (var di = 0; di < 600; di++)
        {
            var doc = GenDoc(r);
            for (var call = 0; call < 5; call++)
            {
                var treeIds = Walk(doc.Root).Select(n => n.Id).ToList();
                var framed = r.Next(6) == 0 ? "absent" : treeIds[r.Next(treeIds.Count)];
                var allKeys = Keys(doc.Root, doc.Floating, doc.Hidden);
                var treeKeys = LayoutEdits.PanelsOf(doc.Root).SelectMany(p => p.Views).ToList();
                string? focus = r.Next(6) switch
                {
                    0 => null,
                    1 => "unknown",
                    2 => doc.Floating.Select(f => f.Panel.Views[0]).DefaultIfEmpty("unknown").First(),
                    3 => doc.Hidden.Select(h => h.Panel.Views[0]).DefaultIfEmpty("unknown").First(),
                    _ => treeKeys.Count == 0 ? null : treeKeys[r.Next(treeKeys.Count)],
                };
                var fresh = r.Next(4) == 0 || allKeys.Count == 0;
                var key = fresh ? "fresh" + _ctr++ : allKeys[r.Next(allKeys.Count)];
                var aspect = r.Next(8) switch
                {
                    0 => 1.0,
                    1 => double.NaN,
                    2 => double.PositiveInfinity,
                    _ => 0.2 + r.NextDouble() * 4.8,
                };
                var share = r.Next(2) == 0;
                var snap = CloneDoc(doc);
                var g = new SequenceIdGenerator();
                var ctx = $"[doc {di} call {call} key={key} framed={framed} focus={focus} A={aspect}] {Dump(doc.Root)}";
                var o = Placement.PlaceNew(doc, key, framed, focus, aspect, g, share);
                Assert.Equal(snap, doc);

                Assert.Equal(!treeIds.Contains(framed), o.Kind == PlacementKind.NeedsFloat);
                if (o.Kind == PlacementKind.NeedsFloat)
                {
                    Assert.Same(doc, o.Document);
                    Assert.Equal(0, g.Calls);
                    continue;
                }
                var nd = o.Document;
                CheckNormalised(nd.Root, ctx);
                var prevKeys = Keys(doc.Root, doc.Floating, doc.Hidden);
                var expKeys = fresh ? prevKeys.Append(key).OrderBy(x => x, StringComparer.Ordinal).ToList() : prevKeys;
                Assert.True(expKeys.SequenceEqual(Keys(nd.Root, nd.Floating, nd.Hidden)), "keys " + ctx);

                var prevPanelIds = PanelIds(doc);
                var expIds = fresh ? prevPanelIds.Append(key).OrderBy(x => x, StringComparer.Ordinal).ToList() : prevPanelIds;
                Assert.True(expIds.SequenceEqual(PanelIds(nd)), "panel ids " + ctx);

                var prevPanel = fresh ? PanelNode.ForWindow(key) : FindPrev(doc, key);
                var inTree = LayoutEdits.PanelsOf(nd.Root).Where(p => p.Id == prevPanel.Id).ToList();
                Assert.True(inTree.Count == 1, "placed once " + ctx);
                Assert.Equal(prevPanel, inTree[0]);

                foreach (var p in LayoutEdits.PanelsOf(nd.Root))
                {
                    var before = fresh && p.Id == key ? p : FindPanelById(doc, p.Id);
                    Assert.True(before is not null && before.Views.SequenceEqual(p.Views)
                        && before.Selected == p.Selected && before.Tabbed == p.Tabbed, "panel changed " + ctx);
                }
                Assert.True(o.Edge is Edge.Right or Edge.Bottom, ctx);
                Assert.Equal(Placement.ChooseAnchor(doc, framed, focus), o.AnchorId);
                Assert.InRange(g.Calls, 1, 2);
                var dBefore = Walk(doc.Root).OfType<DesktopNode>().FirstOrDefault();
                if (dBefore is not null)
                {
                    Assert.Contains(Walk(nd.Root).OfType<DesktopNode>(), d => d.Id == dBefore.Id);
                }
                doc = nd;
            }
        }
    }

    private static PanelNode FindPrev(LayoutDocument d, string key) => LayoutEdits.FindPanelOfWindow(d, key)!;

    private static PanelNode? FindPanelById(LayoutDocument d, string id) =>
        LayoutEdits.PanelsOf(d.Root).Concat(d.Floating.Select(f => f.Panel)).Concat(d.Hidden.Select(h => h.Panel))
            .FirstOrDefault(p => p.Id == id);

    [Fact]
    public void AC29_ArrangeDwindle_Property()
    {
        var r = new Random(777);
        for (var di = 0; di < 600; di++)
        {
            var doc = GenDoc(r);
            var allKeys = Keys(doc.Root, doc.Floating, doc.Hidden);
            var keys = new List<string>();
            var n = r.Next(0, allKeys.Count + 4);
            for (var i = 0; i < n; i++)
            {
                keys.Add(r.Next(8) == 0 ? "unknown" + r.Next(3)
                    : allKeys.Count == 0 ? "unknown" : allKeys[r.Next(allKeys.Count)]);
            }
            var landscape = r.Next(2) == 0;
            var ctx = $"[doc {di} keys={string.Join(",", keys)} landscape={landscape}] {Dump(doc.Root)}";
            var snap = CloneDoc(doc);

            // oracle for B11-B13
            var treePanels = LayoutEdits.PanelsOf(doc.Root);
            var visibleOwner = new Dictionary<string, PanelNode>();
            foreach (var p in treePanels) foreach (var v in p.Views) visibleOwner[v] = p;
            foreach (var f in doc.Floating) foreach (var v in f.Panel.Views) visibleOwner.TryAdd(v, f.Panel);
            var seen = new HashSet<string>();
            var L = new List<PanelNode>();
            foreach (var k in keys)
            {
                if (!seen.Add(k) || !visibleOwner.TryGetValue(k, out var pan)) continue;
                if (L.All(x => x.Id != pan.Id)) L.Add(pan);
            }

            var g = new SequenceIdGenerator();
            var nd = Placement.ArrangeDwindle(doc, keys, landscape, g);
            Assert.Equal(snap, doc);

            var newPanels = LayoutEdits.PanelsOf(nd.Root);
            Assert.True(newPanels.Length == L.Count, "count " + ctx);
            for (var i = 0; i < L.Count; i++) Assert.Same(L[i], newPanels[i]);

            Assert.Equal(L.Count, g.Calls);
            var splits = Walk(nd.Root).OfType<SplitNode>().ToList();
            Assert.True(splits.Select(s => s.Id).SequenceEqual(Enumerable.Range(1, L.Count).Select(i => "n" + i)),
                "split ids " + ctx);

            CheckNormalised(nd.Root, ctx);
            var cur = nd.Root;
            var horizontal = landscape;
            while (cur is SplitNode s)
            {
                Assert.Equal(horizontal ? Axis.X : Axis.Y, s.Axis);
                Assert.True(s.Weights.SequenceEqual(new[] { .5, .5 }), ctx);
                horizontal = !horizontal;
                cur = s.Children[1];
            }
            var leaves = LayoutEdits.LeafIds(nd.Root);
            Assert.Equal("desktop", leaves[^1]);
            Assert.Single(Walk(nd.Root).OfType<DesktopNode>());

            Assert.True(Keys(doc.Root, doc.Floating, null).SequenceEqual(Keys(nd.Root, nd.Floating, null)), "keys " + ctx);
            Assert.True(nd.Hidden.SequenceEqual(doc.Hidden), ctx);
            Assert.Equal(doc.HiddenDesktop, nd.HiddenDesktop);
            Assert.True(PanelIds(doc).SequenceEqual(PanelIds(nd)), "ids " + ctx);

            var included = new HashSet<string>(L.SelectMany(p => p.Views));
            var keptFloats = doc.Floating.Where(f => !f.Panel.Views.Any(included.Contains)).ToList();
            var leftovers = treePanels.Where(p => !p.Views.Any(included.Contains)).ToList();
            Assert.Equal(keptFloats.Count + leftovers.Count, nd.Floating.Length);
            for (var i = 0; i < keptFloats.Count; i++) Assert.Equal(keptFloats[i], nd.Floating[i]);
            var list = new List<int>(keptFloats.Select(f => f.Z));
            for (var i = 0; i < leftovers.Count; i++)
            {
                var f = nd.Floating[keptFloats.Count + i];
                Assert.Same(leftovers[i], f.Panel);
                Assert.Equal(Placement.CascadeRect(i), f.Rect);
                Assert.Null(f.Size);
                var z = list.Count == 0 ? 1 : 1 + list.Max();
                Assert.Equal(z, f.Z);
                list.Add(z);
            }
        }
    }
}
