using System.Reflection;
using System.Xml.Linq;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tests.ServicesSpec;

public sealed class ArchitectureSpecTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CanvasWm.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void AC27_SixInterfacesPublicInServicesNamespace()
    {
        var asm = typeof(IWindowSystem).Assembly;
        foreach (var n in new[] { "IWindowSystem", "ICapture", "IClock", "IRenderer", "ILimitStore", "IDisplayStore" })
        {
            var t = asm.GetType("CanvasWm.Engine.Services." + n);
            Assert.NotNull(t);
            Assert.True(t!.IsInterface);
            Assert.True(t.IsPublic);
        }

        var exported = asm.GetExportedTypes().Select(t => t.Name).ToArray();
        Assert.DoesNotContain("IHotkeys", exported);
        Assert.DoesNotContain("IStore", exported);
    }

    [Fact]
    public void AC27_OnlyCoreReferenced()
    {
        var refs = typeof(IWindowSystem).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(n => n.StartsWith("CanvasWm.", StringComparison.Ordinal))
            .ToHashSet();
        Assert.Equal(new HashSet<string> { "CanvasWm.Core" }, refs);
    }

    [Fact]
    public void AC28_EngineProjectShape()
    {
        var path = Path.Combine(RepoRoot(), "src", "CanvasWm.Engine", "CanvasWm.Engine.csproj");
        var doc = XDocument.Load(path);
        Assert.Equal("net10.0", doc.Descendants("TargetFramework").Single().Value);
        Assert.Empty(doc.Descendants("TargetFrameworks"));
        Assert.Single(doc.Descendants("ProjectReference"));
        Assert.Contains("CanvasWm.Core", doc.Descendants("ProjectReference").Single().Attribute("Include")!.Value);
        Assert.Empty(doc.Descendants("PackageReference"));
    }

    [Fact]
    public void AC28_NoWindowsTfmInEngineOrEngineTests()
    {
        var root = RepoRoot();
        foreach (var p in new[] { "src/CanvasWm.Engine/CanvasWm.Engine.csproj", "tests/CanvasWm.Engine.Tests/CanvasWm.Engine.Tests.csproj" })
        {
            Assert.DoesNotContain("-windows", File.ReadAllText(Path.Combine(root, p)));
        }
    }

    [Fact]
    public void AC28_CleanRoomWordsAbsentFromSrcAndTests()
    {
        var root = RepoRoot();
        string[] needles = { "Trellis" + "WM", "Licen" + "sing" };
        var hits = new List<string>();
        foreach (var top in new[] { "src", "tests" })
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(root, top), "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                if (text.Contains('\0')) continue;
                if (needles.Any(n => text.Contains(n, StringComparison.Ordinal))) hits.Add(rel);
            }
        }

        Assert.Empty(hits);
    }
}
