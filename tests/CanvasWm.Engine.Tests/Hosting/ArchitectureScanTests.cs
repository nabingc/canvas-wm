using System.Reflection;
using System.Text.RegularExpressions;
using CanvasWm.Engine.Hosting;
using CanvasWm.Engine.Persistence;

namespace CanvasWm.Engine.Tests.HostingSpec;

public sealed class ArchitectureScanTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CanvasWm.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    private static IEnumerable<string> Cs(string rel)
    {
        string dir = Path.Combine(Root(), rel);
        char sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep));
    }

    private static string Text(string rel) => File.ReadAllText(Path.Combine(Root(), rel));

    private static List<string> Hits(IEnumerable<string> files, Regex re)
    {
        var hits = new List<string>();
        foreach (string f in files)
        {
            string[] lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
                if (re.IsMatch(lines[i])) hits.Add(Path.GetRelativePath(Root(), f) + ":" + (i + 1) + ": " + lines[i].Trim());
        }

        return hits;
    }

    // ---- AC17 ----

    [Fact]
    public void AC17_ReferencesAndProjectFiles()
    {
        var engineRefs = typeof(DisplayEngine).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).Where(n => n.StartsWith("CanvasWm", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "CanvasWm.Core" }, engineRefs);
        foreach (string p in new[] { "src/CanvasWm.Engine/CanvasWm.Engine.csproj", "src/CanvasWm.Core/CanvasWm.Core.csproj" })
        {
            string t = Text(p);
            Assert.DoesNotContain("PackageReference", t);
            Assert.DoesNotContain("-windows", t);
        }
    }

    [Fact]
    public void AC17_NoIoOrTimeApisInEngineAndCore()
    {
        var re = new Regex(@"\bFile\.|\bDirectory\.|FileStream|StreamWriter|Stopwatch|new Thread\b|System\.Threading\.Timer|Task\.Delay|Thread\.Sleep|DateTime\.(Utc)?Now|Environment\.GetFolderPath|System\.Windows");
        var hits = Hits(Cs("src/CanvasWm.Engine").Concat(Cs("src/CanvasWm.Core")), re);
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    [Fact]
    public void AC17_NoExcludedFeatureNamesInHostingAndPersistence()
    {
        string[] words = { "Ga" + "te", "Lo" + "cked", "Les" + "son", "Up" + "grade", "Licen", "Tri" + "al", "Wel" + "come", "Onboar" + "d" };
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var bad = new List<string>();
        foreach (var type in typeof(DisplayEngine).Assembly.GetTypes()
                     .Where(x => (x.Namespace == "CanvasWm.Engine.Hosting" || x.Namespace == "CanvasWm.Engine.Persistence")
                                 && (x.IsPublic || x.IsNestedPublic)))
        {
            var names = new List<string> { type.Name };
            foreach (var m in type.GetMembers(all))
            {
                if (m is MethodBase mb && !mb.IsPublic && !mb.IsFamily) continue;
                if (m is FieldInfo fi && !fi.IsPublic && !fi.IsFamily) continue;   // spec: public members only
                names.Add(m.Name);
                if (m is MethodBase meth) names.AddRange(meth.GetParameters().Select(p => p.Name ?? ""));
            }

            bad.AddRange(names.Where(n => words.Any(w => n.Contains(w, StringComparison.Ordinal))).Select(n => type.Name + "." + n));
        }

        Assert.Empty(bad);
    }

    [Fact]
    public void AC17_NoLegacyInterfaceNames()
    {
        var names = typeof(DisplayEngine).Assembly.GetExportedTypes().Select(t => t.Name).ToHashSet();
        Assert.DoesNotContain("IStore", names);
        Assert.DoesNotContain("IHotkeys", names);
    }

    [Fact]
    public void AC17_HostingAndPersistenceTypesAreExported()
    {
        var exported = typeof(DisplayEngine).Assembly.GetExportedTypes().Where(t => t.Namespace == "CanvasWm.Engine.Hosting")
            .Select(t => t.Name).ToHashSet();
        foreach (var n in new[] { "LoopClock", "NullRenderer", "NullCapture", "TickPacingOptions", "TickSource", "TickPacer",
                                  "TickCoalescer", "ShortcutRelay", "QuitReason", "LifetimeState", "AppLifetime", "HostComposition" })
            Assert.Contains(n, exported);
        var pers = typeof(DisplayEngine).Assembly.GetExportedTypes().Where(t => t.Namespace == "CanvasWm.Engine.Persistence")
            .Select(t => t.Name).ToHashSet();
        foreach (var n in new[] { "ITextFileStore", "TextFileStatus", "TextFileRead", "TextFileNames", "DataFiles", "SavedDisplayJson",
                                  "LimitsJson", "SettingsJson", "FileDisplayStore", "FileLimitStore", "HostSettings", "SettingsFile" })
            Assert.Contains(n, pers);
    }

    // ---- AC18 ----

    [Fact]
    public void AC18_AppProjectFile()
    {
        string t = Text("src/CanvasWm.App/CanvasWm.App.csproj");
        Assert.Contains("<TargetFramework>net10.0-windows</TargetFramework>", t);
        Assert.Contains("<OutputType>WinExe</OutputType>", t);
        Assert.Contains("<UseWindowsForms>true</UseWindowsForms>", t);
        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", t);
        Assert.Contains("<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>", t);
        Assert.Matches(@"<NoWarn>[^<]*WFAC010", t);
        Assert.DoesNotContain("PackageReference", t);
        var refs = Regex.Matches(t, @"<ProjectReference Include=""([^""]+)""").Select(m => Path.GetFileName(m.Groups[1].Value.Replace('\\', '/'))).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "CanvasWm.Core.csproj", "CanvasWm.Engine.csproj", "CanvasWm.Win32.csproj" }, refs);
    }

    [Fact]
    public void AC18_ManifestAndProgram()
    {
        string m = Text("src/CanvasWm.App/app.manifest");
        Assert.Contains("PerMonitorV2", m);
        Assert.Contains("true/pm", m);
        Assert.Contains("asInvoker", m);
        string p = Text("src/CanvasWm.App/Program.cs");
        Assert.Contains("[STAThread]", p);
        Assert.Contains("Application.Run(", p);
    }

    [Fact]
    public void AC18_Win32HasNoWinForms()
    {
        string proj = Text("src/CanvasWm.Win32/CanvasWm.Win32.csproj");
        Assert.DoesNotContain("UseWindowsForms", proj);
        Assert.DoesNotContain("UseWPF", proj);
        var hits = Hits(Cs("src/CanvasWm.Win32"), new Regex(@"System\.Windows\.Forms"));
        Assert.Empty(hits);
    }

    [Fact]
    public void AC18_Win32HygieneScansStillEmpty()
    {
        var w = Cs("src/CanvasWm.Win32").ToList();
        Assert.Empty(Hits(w, new Regex(@"ClassifierPolicy|WindowClassifier|TrackerOptions|WindowTracker|IgnoreReason|TrackedFate|ExcludedApps|MinimumSize")));
        Assert.Empty(Hits(w, new Regex(@"(?<![\w.])(120|80|0\.05|0\.4|3\.0|0\.3|0\.6)(?![\w.])")));
        Assert.Empty(Hits(w, new Regex(@"System\.Threading\.Timer|PeriodicTimer|Task\.Delay|Thread\.Sleep|Stopwatch|DispatcherTimer|SetTimer")));
        Assert.Empty(Hits(w, new Regex(@"DllImport")));
        Assert.Empty(Hits(w, new Regex(@"SendMessage|(?<!Internal)GetWindowText|GetSystemMenu|SHGetPropertyStoreForWindow|ComImport")));
        Assert.Contains(w, f => File.ReadAllText(f).Contains("LibraryImport", StringComparison.Ordinal));
    }

    [Fact]
    public void AC18_NoLayoutOrPolicyInApp()
    {
        var app = Cs("src/CanvasWm.App").ToList();
        Assert.NotEmpty(app);
        Assert.Empty(Hits(app, new Regex(@"\.(Move|ZoomIn|ZoomOut|ToggleOverview|ArrangeAll|ReleaseAll|VisibleAreaChanged|Suspend|Resume)\(")));
        Assert.Empty(Hits(app, new Regex(@"(?<![\w.])(1\.5|0\.4|0\.8|2\.0)(?![\w.])")));
    }

    [Fact]
    public void AC18_NoForbiddenProductNames()
    {
        var re = new Regex("Trellis" + "WM|Licen" + "sing");
        var files = Cs("src").Concat(Cs("tests")).ToList();
        Assert.Empty(Hits(files, re));
    }
}
