using System.Reflection;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Tests.TuningSpec;

public sealed class TuningShapeTests
{
    [Fact]
    public void AC4_Type_identity()
    {
        var t = typeof(Tuning);
        Assert.True(t.IsClass && t.IsSealed);
        Assert.Equal("CanvasWm.Core", t.Namespace);
        Assert.Equal("CanvasWm.Core", t.Assembly.GetName().Name);
    }

    [Fact]
    public void AC4_Properties_names_and_types()
    {
        var props = TuningData.InstanceProps();
        Assert.Equal(50, props.Length);
        Assert.Equal(TuningData.Defaults.Keys.OrderBy(x => x), props.Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(45, props.Count(p => p.PropertyType == typeof(double)));
        Assert.Equal(new[] { "CollapsedOutlineWidth", "SnapBackLimit" },
            props.Where(p => p.PropertyType == typeof(int)).Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(new[] { "CompactSize", "FixedTileMinimum", "TileMinimumFloor" },
            props.Where(p => p.PropertyType == typeof(Size)).Select(p => p.Name).OrderBy(x => x));
    }

    [Fact]
    public void AC4_Single_static_property_Default_without_setter()
    {
        var s = typeof(Tuning).GetProperties(BindingFlags.Public | BindingFlags.Static);
        var d = Assert.Single(s);
        Assert.Equal("Default", d.Name);
        Assert.Equal(typeof(Tuning), d.PropertyType);
        Assert.Null(d.SetMethod);
    }

    [Fact]
    public void AC4_No_public_fields_and_static_fields_readonly()
    {
        Assert.Empty(typeof(Tuning).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        var statics = typeof(Tuning).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert.All(statics, f => Assert.True(f.IsInitOnly, f.Name));
    }

    [Fact]
    public void AC4_No_type_or_namespace_named_Tuning_under_Core()
    {
        var asm = typeof(Tuning).Assembly;
        Assert.DoesNotContain(asm.GetTypes(), x => x.FullName == "CanvasWm.Core.Tuning" && x != typeof(Tuning));
        Assert.DoesNotContain(asm.GetTypes(), x => x.Namespace is { } ns &&
            (ns == "CanvasWm.Core.Tuning" || ns.StartsWith("CanvasWm.Core.Tuning.", StringComparison.Ordinal)));
    }

    [Fact]
    public void AC4_No_excluded_or_deferred_concept_names()
    {
        foreach (var p in TuningData.InstanceProps())
        {
            Assert.DoesNotContain("Settle", p.Name);
            Assert.DoesNotContain("Tuck", p.Name);
            Assert.DoesNotContain("Dim", p.Name);
            Assert.NotEqual("DropHold", p.Name);
            Assert.NotEqual("DesktopEdge", p.Name);
            Assert.NotEqual("DesktopEdgeBandFraction", p.Name);
        }
    }

    [Fact]
    public void AC5_All_properties_init_only()
    {
        foreach (var p in TuningData.InstanceProps())
        {
            Assert.NotNull(p.GetMethod);
            Assert.True(p.GetMethod!.IsPublic, p.Name);
            Assert.NotNull(p.SetMethod);
            Assert.True(p.SetMethod!.IsPublic, p.Name);
            var mods = p.SetMethod.ReturnParameter.GetRequiredCustomModifiers();
            Assert.Contains(mods, m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        }
    }

    [Fact]
    public void AC9_Class_lists_partition_the_property_set()
    {
        var all = TuningData.AllClassed().ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(13, TuningData.Pos.Length);
        Assert.Equal(30, TuningData.NonNeg.Length);
        Assert.Equal(50, all.Count);
        Assert.Equal(TuningData.InstanceProps().Select(p => p.Name).OrderBy(x => x), all.OrderBy(x => x));
    }
}
