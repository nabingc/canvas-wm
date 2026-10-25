using System.Reflection;
using System.Text.RegularExpressions;
using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Tracking;

namespace CanvasWm.Engine.Tests.RoutingSpec;

public sealed class ArchitectureTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CanvasWm.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    [Fact]
    public void AC28_RouterImplementsTrackerTarget()
    {
        Assert.True(typeof(IWindowTrackerTarget).IsAssignableFrom(typeof(EngineRouter)));
    }

    [Fact]
    public void AC28_AssemblyReferences()
    {
        var engineRefs = typeof(DisplayEngine).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).Where(n => n.StartsWith("CanvasWm", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "CanvasWm.Core" }, engineRefs);
        var coreRefs = typeof(CanvasWm.Core.Geometry.Rect).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).Where(n => n.StartsWith("CanvasWm", StringComparison.Ordinal)).ToArray();
        Assert.Empty(coreRefs);
    }

    [Fact]
    public void AC28_ExclusionRuleNamesAbsent()
    {
        string[] words =
        {
            "Ga" + "te", "Lo" + "cked", "Les" + "son", "Up" + "grade", "Licen", "Tri" + "al", "Wel" + "come", "Onboar" + "d",
        };
        var asm = typeof(EngineRouter).Assembly;
        var bad = new List<string>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in asm.GetTypes().Where(x => x.Namespace == "CanvasWm.Engine.Routing" && (x.IsPublic || x.IsNestedPublic)))
        {
            var names = new List<string> { type.Name };
            foreach (var m in type.GetMembers(all))
            {
                if (m is MethodBase mb && !mb.IsPublic && !mb.IsFamily) continue;
                names.Add(m.Name);
                if (m is MethodBase meth)
                {
                    names.AddRange(meth.GetParameters().Select(p => p.Name ?? ""));
                }
            }

            foreach (var n in names)
            {
                if (words.Any(w => n.Contains(w, StringComparison.Ordinal))) bad.Add(type.Name + "." + n);
            }
        }

        Assert.Empty(bad);
    }

    [Fact]
    public void AC28_DefaultTimingLiteralsOnlyInRouterOptions()
    {
        string dir = Path.Combine(Root(), "src", "CanvasWm.Engine", "Routing");
        var files = Directory.GetFiles(dir, "*.cs").Where(f => Path.GetFileName(f) != "RouterOptions.cs").ToList();
        Assert.NotEmpty(files);
        var hits = new List<string>();
        var literal = new Regex(@"(?<![\w.])(1\.5|0\.4|0\.8|2\.0)(?![\w])");
        foreach (var f in files)
        {
            foreach (var line in File.ReadAllLines(f))
            {
                string code = line;
                int c = code.IndexOf("//", StringComparison.Ordinal);
                if (c >= 0) code = code[..c];
                if (literal.IsMatch(code)) hits.Add(Path.GetFileName(f) + ": " + line.Trim());
            }
        }

        Assert.Empty(hits);
        Assert.Contains("1.5", File.ReadAllText(Path.Combine(dir, "RouterOptions.cs")));
    }

    [Fact]
    public void AC28_CleanRoomWordsAbsentFromNewFiles()
    {
        string[] needles = { "Trellis" + "WM", "Licen" + "sing" };
        var root = Root();
        var hits = new List<string>();
        foreach (var top in new[] { "src", "tests", "specs" })
        {
            var baseDir = Path.Combine(root, top);
            if (!Directory.Exists(baseDir)) continue;
            foreach (var f in Directory.EnumerateFiles(baseDir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                var text = File.ReadAllText(f);
                if (needles.Any(n => text.Contains(n, StringComparison.Ordinal))) hits.Add(rel);
            }
        }

        Assert.Empty(hits);
    }
}
