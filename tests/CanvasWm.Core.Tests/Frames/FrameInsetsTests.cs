using CanvasWm.Core.Frames;

namespace CanvasWm.Core.Tests.FramesSpec;

public sealed class FrameInsetsTests
{
    [Fact]
    public void AC4_measure()
    {
        Assert.Equal(new FrameInsets(7, 0, 7, 7), FrameInsets.Measure(93, 100, 907, 707, 100, 100, 900, 700));
    }

    [Fact]
    public void AC4_measure_clamps_negatives()
    {
        Assert.Equal(new FrameInsets(0, 0, 0, 10), FrameInsets.Measure(110, 0, 900, 700, 100, 0, 910, 690));
    }

    [Fact]
    public void AC4_measure_uses_64_bit()
    {
        FrameInsets i = FrameInsets.Measure(int.MinValue, 0, 0, 0, int.MaxValue, 0, 0, 0);
        Assert.Equal(int.MaxValue, i.Left);
    }

    [Fact]
    public void AC4_zero()
    {
        Assert.Equal(new FrameInsets(0, 0, 0, 0), FrameInsets.Zero);
    }

    [Theory]
    [InlineData(96u, 144u, 11, 0, 11, 11)]
    [InlineData(96u, 120u, 9, 0, 9, 9)]
    [InlineData(96u, 96u, 7, 0, 7, 7)]
    public void AC4_rescale_from_7_0_7_7(uint from, uint to, int l, int t, int r, int b)
    {
        Assert.Equal(new FrameInsets(l, t, r, b), new FrameInsets(7, 0, 7, 7).Rescale(from, to));
    }

    [Fact]
    public void AC4_rescale_back()
    {
        Assert.Equal(new FrameInsets(7, 0, 7, 7), new FrameInsets(11, 0, 11, 11).Rescale(144, 96));
    }

    [Fact]
    public void AC4_rescale_zero_dpi_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameInsets(7, 0, 7, 7).Rescale(0, 96));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameInsets(7, 0, 7, 7).Rescale(96, 0));
    }
}
