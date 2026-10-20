using System.Reflection;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Windows;

namespace CanvasWm.Core.Tests.WindowsSpec;

public sealed class PolicyAndFactsTests
{
    [Fact]
    public void AC7_Defaults()
    {
        Assert.Equal(new Size(120, 80), ClassifierPolicy.Default.MinimumSize);
        Assert.Empty(ClassifierPolicy.Default.ExcludedApps);
        Assert.Same(ClassifierPolicy.Default, ClassifierPolicy.Default);
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(1, double.PositiveInfinity)]
    [InlineData(-1, 1)]
    [InlineData(1, -0.5)]
    public void AC7_RejectedSizes(double w, double h)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ClassifierPolicy { MinimumSize = new Size(w, h) });
        Assert.Equal("MinimumSize", ex.ParamName);
    }

    [Fact]
    public void AC7_ZeroSizeAccepted() =>
        Assert.Equal(new Size(0, 0), (new ClassifierPolicy { MinimumSize = new Size(0, 0) }).MinimumSize);

    [Fact]
    public void AC7_RejectedExclusions()
    {
        Assert.Throws<ArgumentNullException>(() => new ClassifierPolicy { ExcludedApps = null! });
        Assert.Throws<ArgumentException>(() => ClassifierPolicy.Default.WithExcludedApps(new string[] { null! }));
        Assert.Throws<ArgumentException>(() => ClassifierPolicy.Default.WithExcludedApps(new[] { "" }));
    }

    [Fact]
    public void AC7_Equality()
    {
        var a = ClassifierPolicy.Default.WithExcludedApps(new[] { "a", "b" });
        var b = ClassifierPolicy.Default.WithExcludedApps(new[] { "b", "a" });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, ClassifierPolicy.Default.WithExcludedApps(new[] { "a" }));
    }

    [Fact]
    public void AC7_CopiesSourceAndDefaultUnchanged()
    {
        var src = new List<string> { "a" };
        var p = ClassifierPolicy.Default.WithExcludedApps(src);
        src.Add("b");
        Assert.Single(p.ExcludedApps);
        var q = ClassifierPolicy.Default with { MinimumSize = new Size(1, 1) };
        Assert.NotSame(q, ClassifierPolicy.Default);
        Assert.Equal(new Size(120, 80), ClassifierPolicy.Default.MinimumSize);
        Assert.Empty(ClassifierPolicy.Default.ExcludedApps);
    }

    [Fact]
    public void AC7_ExclusionIsOrdinalCaseSensitive()
    {
        var p = ClassifierPolicy.Default.WithExcludedApps(new[] { "a" });
        Assert.Contains("a", p.ExcludedApps);
        Assert.DoesNotContain("A", p.ExcludedApps);
    }

    [Fact]
    public void AC8_FactsDefaults()
    {
        var f = new OsWindowFacts();
        Assert.Equal("", f.Title);
        Assert.Null(f.Frame);
        Assert.Equal(CloakKind.None, f.Cloak);
        Assert.Equal(0UL, f.Handle);
    }

    [Fact]
    public void AC8_NullTitleThrows() =>
        Assert.Throws<ArgumentNullException>(() => new OsWindowFacts { Title = null! });

    [Fact]
    public void AC8_ValueEquality()
    {
        Assert.Equal(ClassifierTests.Std(1, 7), ClassifierTests.Std(1, 7));
        Assert.NotEqual(ClassifierTests.Std(1, 7), ClassifierTests.Std(2, 7));
    }

    [Fact]
    public void AC8_DefaultFactsIgnoredNoHandle()
    {
        var c = WindowClassifier.Classify(new OsWindowFacts(), ClassifierPolicy.Default);
        Assert.Equal(WindowClassification.Ignore(IgnoreReason.NoHandle), c);
    }

    [Fact]
    public void AC8_IgnoreNoneAndUndefinedThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowClassification.Ignore(IgnoreReason.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowClassification.Ignore((IgnoreReason)999));
        var i = WindowClassification.Ignore(IgnoreReason.Hung);
        Assert.False(i.IsManaged);
        Assert.False(i.Dockable);
        var m = WindowClassification.Managed(false);
        Assert.True(m.IsManaged);
        Assert.Equal(IgnoreReason.None, m.Reason);
    }

    [Fact]
    public void AC30_CoreWindowsTypesDoNotReferenceEngine()
    {
        var asm = typeof(OsWindowFacts).Assembly;
        Assert.DoesNotContain(asm.GetReferencedAssemblies(), n => n.Name!.Contains("Engine") || n.Name.Contains("Win32"));
        foreach (var t in asm.GetTypes().Where(t => t.Namespace == "CanvasWm.Core.Windows"))
        {
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                var types = m switch
                {
                    PropertyInfo p => new[] { p.PropertyType },
                    FieldInfo f => new[] { f.FieldType },
                    MethodBase mb => mb.GetParameters().Select(x => x.ParameterType)
                        .Concat(mb is MethodInfo mi ? new[] { mi.ReturnType } : Array.Empty<Type>()).ToArray(),
                    _ => Array.Empty<Type>(),
                };
                Assert.DoesNotContain(types, x => x.Namespace?.StartsWith("CanvasWm.Engine") == true);
            }
        }
    }
}
