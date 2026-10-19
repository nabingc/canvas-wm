using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;

namespace CanvasWm.Core.Tests.DockingSpec;

public sealed class SequenceIdGenerator : IIdGenerator
{
    public int Calls { get; private set; }
    public List<string> Prefixes { get; } = new();

    public string Make(string prefix)
    {
        Calls++;
        Prefixes.Add(prefix);
        return "n" + Calls;
    }
}

public sealed record TestRestore : RestoreTarget;

internal static class T
{
    public const double Tol = 1e-9;

    public static PanelNode P(string key) => PanelNode.ForWindow(key);
    public static DesktopNode D(LayoutNode? child = null, string id = "desktop") => new(child, id);

    public static SplitNode S(string id, Axis axis, params (LayoutNode Node, double Weight)[] c) =>
        new(id, axis, c.Select(x => x.Weight), c.Select(x => x.Node));

    public static SplitNode X(string id, params (LayoutNode, double)[] c) => S(id, Axis.X, c);
    public static SplitNode Y(string id, params (LayoutNode, double)[] c) => S(id, Axis.Y, c);

    public static LayoutDocument Doc(LayoutNode? root) => new(root);

    /// <summary>Structural comparison with weight tolerance.</summary>
    public static void AssertTree(LayoutNode? expected, LayoutNode? actual, string path = "root")
    {
        if (expected is null)
        {
            Assert.True(actual is null, $"{path}: expected null, got {actual}");
            return;
        }
        Assert.True(actual is not null, $"{path}: expected {expected}, got null");
        Assert.Equal(expected.GetType(), actual!.GetType());
        Assert.True(expected.Id == actual.Id, $"{path}: id expected {expected.Id} actual {actual.Id}");
        switch (expected)
        {
            case PanelNode p:
                Assert.Equal(p, (PanelNode)actual);
                break;
            case DesktopNode d:
                AssertTree(d.Child, ((DesktopNode)actual).Child, path + "/" + d.Id);
                break;
            case SplitNode s:
                var a = (SplitNode)actual;
                Assert.True(s.Axis == a.Axis, $"{path}: axis expected {s.Axis} actual {a.Axis}");
                Assert.True(s.Children.Length == a.Children.Length,
                    $"{path}: child count expected {s.Children.Length} actual {a.Children.Length}: {Dump(actual)}");
                Assert.Equal(s.Children.Length, a.Weights.Length);
                for (var i = 0; i < s.Children.Length; i++)
                {
                    Assert.True(Math.Abs(s.Weights[i] - a.Weights[i]) <= Tol,
                        $"{path}: weight[{i}] expected {s.Weights[i]} actual {a.Weights[i]}: {Dump(actual)}");
                    AssertTree(s.Children[i], a.Children[i], path + "/" + s.Id + "[" + i + "]");
                }
                break;
        }
    }

    public static string Dump(LayoutNode? n) => n switch
    {
        null => "null",
        PanelNode p => p.Id,
        DesktopNode d => "D(" + d.Id + "){" + Dump(d.Child) + "}",
        SplitNode s => s.Axis + "(" + s.Id + ")[" + string.Join(", ", s.Children.Select((c, i) => Dump(c) + " " + s.Weights[i].ToString("0.####"))) + "]",
        _ => "?",
    };

    public static IEnumerable<LayoutNode> Walk(LayoutNode? n)
    {
        if (n is null) yield break;
        yield return n;
        if (n is SplitNode s)
            foreach (var c in s.Children)
                foreach (var d in Walk(c)) yield return d;
        if (n is DesktopNode d2 && d2.Child is not null)
            foreach (var d in Walk(d2.Child)) yield return d;
    }

    public static LayoutNode Clone(LayoutNode n) => n switch
    {
        SplitNode s => new SplitNode(s.Id, s.Axis, s.Weights.ToArray(), s.Children.Select(Clone).ToArray()),
        PanelNode p => new PanelNode(p.Id, p.Views.ToArray(), p.Selected, p.Tabbed),
        DesktopNode d => new DesktopNode(d.Child is null ? null : Clone(d.Child), d.Id),
        _ => throw new InvalidOperationException(),
    };

    public static LayoutDocument CloneDoc(LayoutDocument d) => new(
        d.Root is null ? null : Clone(d.Root),
        d.Floating.Select(f => f with { Panel = (PanelNode)Clone(f.Panel) }).ToArray(),
        d.Hidden.Select(h => h with { Panel = (PanelNode)Clone(h.Panel) }).ToArray(),
        d.HiddenDesktop);

    public static FloatingPanel Fl(PanelNode p, int z = 0) => new(p, new Rect(0, 0, 10, 10), z);
    public static HiddenPanel Hd(PanelNode p) => new(p, new TestRestore());
}
