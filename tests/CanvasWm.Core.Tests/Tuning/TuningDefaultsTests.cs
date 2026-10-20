using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Tests.TuningSpec;

public sealed class TuningDefaultsTests
{
    public static IEnumerable<object[]> DefaultRows() =>
        TuningData.Defaults.Select(kv => new object[] { kv.Key });

    [Theory]
    [MemberData(nameof(DefaultRows))]
    public void AC1_Default_property_has_mapped_default(string name)
    {
        var expected = TuningData.Defaults[name];
        var p = typeof(Tuning).GetProperty(name)!;
        Assert.Equal(expected, p.GetValue(Tuning.Default));
        Assert.Equal(expected, p.GetValue(new Tuning()));
    }

    [Fact]
    public void AC1_Table_has_50_entries() => Assert.Equal(50, TuningData.Defaults.Count);

    [Fact]
    public void AC2_Pinned_corrected_values()
    {
        var t = Tuning.Default;
        Assert.True(t.LandingRevealQuietSeconds == 0.6);
        Assert.True(t.ReflowTimeoutSeconds == 1.2);
        Assert.True(t.ZoomStepDelta == 0.15);
        Assert.True(t.GapDragRestSeconds == 0.15);
        Assert.True(t.NativeTabRecheckSeconds == 0.3);
    }

    [Fact]
    public void AC2_Capture_intervals()
    {
        var t = Tuning.Default;
        Assert.Equal(5.0, 1 / t.LivePicturesPerSecond, 1e-12);
        Assert.Equal(6.0, 1 / t.PreviewPicturesPerSecond, 1e-12);
        Assert.Equal(30.0, 1 / t.UnseenPicturesPerSecond, 1e-12);
        Assert.Equal(60.0, 1 / t.AwayPicturesPerSecond, 1e-12);
        Assert.Equal(0.1, 1 / t.ChangingPicturesPerSecond, 1e-12);
        Assert.Equal(1.0 / 3.0, 1 / t.MovingPicturesPerSecond, 1e-12);
        Assert.Equal(960.0, t.FloatMaxFraction * 1440, 1e-12);
    }

    [Fact]
    [Trait("Assumption", "A4")]
    public void AC1_A4_ratio_defaults_are_csharp_divisions()
    {
        var t = Tuning.Default;
        Assert.Equal(BitConverter.DoubleToInt64Bits(2.0 / 3.0), BitConverter.DoubleToInt64Bits(t.FloatMaxFraction));
        Assert.Equal(BitConverter.DoubleToInt64Bits(1.0 / 6.0), BitConverter.DoubleToInt64Bits(t.PreviewPicturesPerSecond));
        Assert.Equal(BitConverter.DoubleToInt64Bits(1.0 / 30.0), BitConverter.DoubleToInt64Bits(t.UnseenPicturesPerSecond));
        Assert.Equal(BitConverter.DoubleToInt64Bits(1.0 / 60.0), BitConverter.DoubleToInt64Bits(t.AwayPicturesPerSecond));
    }

    [Fact]
    public void AC3_Default_is_single_instance_and_equals_new()
    {
        Assert.True(ReferenceEquals(Tuning.Default, Tuning.Default));
        Assert.True(Tuning.Default == new Tuning());
        Assert.Equal(Tuning.Default.GetHashCode(), new Tuning().GetHashCode());
    }

    [Fact]
    public void AC6_With_expressions()
    {
        var t = Tuning.Default with { CameraMoveSeconds = 0.25, SnapBackLimit = 5, CompactSize = new Size(300, 200) };
        Assert.Equal(0.25, t.CameraMoveSeconds);
        Assert.Equal(5, t.SnapBackLimit);
        Assert.Equal(new Size(300, 200), t.CompactSize);
        var changed = new HashSet<string> { "CameraMoveSeconds", "SnapBackLimit", "CompactSize" };
        var props = TuningData.InstanceProps();
        Assert.Equal(50, props.Length);
        foreach (var p in props.Where(p => !changed.Contains(p.Name)))
            Assert.Equal(p.GetValue(Tuning.Default), p.GetValue(t));
        Assert.True(t != Tuning.Default);
        Assert.True(Tuning.Default == new Tuning());

        var back = t with { CameraMoveSeconds = 0.5, SnapBackLimit = 3, CompactSize = new Size(380, 260) };
        Assert.True(back == Tuning.Default);
    }
}
