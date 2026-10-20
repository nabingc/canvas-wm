using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Tests.TuningSpec;

public sealed class TuningValidationTests
{
    private static readonly object[] BadGeneral =
        { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.001 };

    private static IEnumerable<string> DoubleClasses =>
        TuningData.Pos.Concat(TuningData.NonNeg).Concat(TuningData.Band).Concat(TuningData.Cap);

    public static IEnumerable<object[]> RejectRows()
    {
        foreach (var r in TuningData.Rows(DoubleClasses, BadGeneral)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.Pos, 0.0, -0.0)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.Band, 0.5000001, 1.0)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.Cap, 0.0, -0.0, 1.0000001)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.SizeNonNeg,
            new Size(double.NaN, 1), new Size(1, double.NaN), new Size(-1, 1), new Size(1, -1),
            new Size(double.PositiveInfinity, 1)))
            yield return r;
        foreach (var r in TuningData.Rows(TuningData.IntAtLeast1, 0, -1)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.IntNonNeg, -1)) yield return r;
    }

    [Theory]
    [MemberData(nameof(RejectRows))]
    public void AC7_Invalid_value_throws_with_param_name(string name, object value, bool useWith)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TuningData.Apply(useWith, name, value));
        Assert.Equal(name, ex.ParamName);
        Assert.True(Tuning.Default == new Tuning());
    }

    public static IEnumerable<object[]> AcceptRows()
    {
        foreach (var r in TuningData.Rows(TuningData.NonNeg, 0.0, 1e9)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.Pos, double.Epsilon, 1e9)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.Band, 0.0, 0.5)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.Cap, double.Epsilon, 1.0)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.SizeNonNeg, new Size(0, 0))) yield return r;
        foreach (var r in TuningData.Rows(TuningData.IntAtLeast1, 1, int.MaxValue)) yield return r;
        foreach (var r in TuningData.Rows(TuningData.IntNonNeg, 0, int.MaxValue)) yield return r;
    }

    [Theory]
    [MemberData(nameof(AcceptRows))]
    public void AC8_Boundary_value_accepted_and_read_back(string name, object value, bool useWith)
    {
        var t = TuningData.Apply(useWith, name, value);
        Assert.Equal(value, typeof(Tuning).GetProperty(name)!.GetValue(t));
    }

    [Fact]
    public void AC8_Negative_zero_accepted_for_PickupSeconds()
    {
        var t = new Tuning { PickupSeconds = -0.0 };
        Assert.True(double.IsNegative(t.PickupSeconds));
        Assert.Equal(0.0, t.PickupSeconds);
    }

    [Fact]
    public void AC8_Negative_zero_accepted_by_Band_and_Size()
    {
        Assert.Equal(0.0, new Tuning { TileEdgeBandFraction = -0.0 }.TileEdgeBandFraction);
        Assert.Equal(0.0, new Tuning { CompactSize = new Size(-0.0, -0.0) }.CompactSize.Width);
    }

    [Fact]
    [Trait("Assumption", "A6")]
    public void AC8_A6_Cross_field_inverted_settings_accepted()
    {
        var t = new Tuning { UncoverAspectMismatch = 0.02 };
        Assert.Equal(0.02, t.UncoverAspectMismatch);
        Assert.Equal(0.01, t.CoverAspectMismatch);
        var u = Tuning.Default with { TileMinimumFloor = new Size(600, 400) };
        Assert.Equal(new Size(600, 400), u.TileMinimumFloor);
    }

    [Fact]
    [Trait("Assumption", "A5")]
    public void AC8_A5_Zero_pickup_accepted_zero_camera_rejected()
    {
        Assert.Equal(0.0, new Tuning { PickupSeconds = 0 }.PickupSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tuning { CameraMoveSeconds = 0 });
    }

    [Fact]
    public void AC7_Compile_time_with_and_initialiser_forms()
    {
        var a = Assert.Throws<ArgumentOutOfRangeException>(() => Tuning.Default with { SnapBackLimit = 0 });
        Assert.Equal("SnapBackLimit", a.ParamName);
        var b = Assert.Throws<ArgumentOutOfRangeException>(() => new Tuning { FloatMaxFraction = 1.0000001 });
        Assert.Equal("FloatMaxFraction", b.ParamName);
        Assert.True(Tuning.Default == new Tuning());
    }
}
