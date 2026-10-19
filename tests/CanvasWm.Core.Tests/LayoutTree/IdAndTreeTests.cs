using System.Text.RegularExpressions;
using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.LayoutTreeSpec.H;

namespace CanvasWm.Core.Tests.LayoutTreeSpec;

public sealed class IdAndTreeTests
{
    [Fact]
    public void AC31_FindDesktop()
    {
        Assert.Null(LayoutTree.FindDesktop(null));
        var d1 = new DesktopNode(null, "d1"); var d2 = new DesktopNode(null, "d2");
        var tree = X("x", (P("a"), 1), (Y("y", (P("b"), 1), (d1, 1)), 1), (d2, 1));
        Assert.Same(d1, LayoutTree.FindDesktop(tree));
        var d0 = new DesktopNode(X("x", (P("a"), 1), (d1, 1)), "d0");
        Assert.Same(d0, LayoutTree.FindDesktop(d0));
        Assert.Null(LayoutTree.FindDesktop(P("a")));
    }

    [Fact]
    public void AC32_Base36()
    {
        Assert.Equal("0", UidGenerator.ToBase36(0));
        Assert.Equal("z", UidGenerator.ToBase36(35));
        Assert.Equal("10", UidGenerator.ToBase36(36));
        Assert.Equal("lflr", UidGenerator.ToBase36(999_999));
        Assert.Equal("1z141z3", UidGenerator.ToBase36(uint.MaxValue));
    }

    [Fact]
    public void AC33_Make()
    {
        var g = new UidGenerator(() => 0);
        Assert.Equal("split-01", g.Make("split"));
        Assert.Equal("split-02", g.Make("split"));
        Assert.Equal("split-1z1411", new UidGenerator(() => uint.MaxValue).Make("split"));
    }

    [Fact]
    public void AC34_Wrap()
    {
        var g = new UidGenerator(() => 0, initialCounter: 999_998);
        Assert.Equal("split-0lflr", g.Make("split"));
        Assert.Equal("split-00", g.Make("split"));
        Assert.Equal("split-01", g.Make("split"));
    }

    [Fact]
    public void AC35_RandomOncePerMake()
    {
        var n = 0;
        var g = new UidGenerator(() => { n++; return 5; });
        for (var i = 1; i <= 10; i++) { g.Make("p"); Assert.Equal(i, n); }
    }

    [Fact]
    public async Task AC36_ThreadSafe()
    {
        var g = new UidGenerator(() => 7);
        var bag = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++) bag.Add(g.Make("split"));
        })).ToArray();
        await Task.WhenAll(tasks);
        Assert.Equal(80_000, bag.Distinct().Count());
    }

    [Fact]
    public void AC37_SharedAndErrors()
    {
        Assert.Matches(new Regex("^split-[0-9a-z]{1,5}[0-9a-z]{1,4}$"), UidGenerator.Shared.Make(IdPrefixes.Split));
        Assert.Throws<ArgumentNullException>(() => new UidGenerator(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UidGenerator(() => 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UidGenerator(() => 0, 1_000_000));
        Assert.Throws<ArgumentNullException>(() => new UidGenerator(() => 0).Make(null!));
        Assert.Equal("split", IdPrefixes.Split);
    }

    [Fact]
    public void AC38_CoreCsprojHasNoReferences()
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "src", "CanvasWm.Core", "CanvasWm.Core.csproj");
            if (File.Exists(c)) { path = c; break; }
        }
        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        Assert.DoesNotContain("PackageReference", text);
        Assert.DoesNotContain("ProjectReference", text);
        // build exit code / zero warnings verified via `dotnet build`; CoreReferencesNoCanvasWmAssembly in SkeletonTests
    }
}
