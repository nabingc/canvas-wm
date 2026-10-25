using CanvasWm.Core.Frames;

namespace CanvasWm.Core.Tests.FramesSpec;

public sealed class PhysicalCoordinatesTests
{
    [Fact]
    public void AC5_limit() => Assert.Equal(30000, PhysicalCoordinates.Limit);

    [Theory]
    [InlineData(-32000, -30000)]
    [InlineData(30001, 30000)]
    [InlineData(1919, 1919)]
    [InlineData(int.MinValue, -30000)]
    [InlineData(int.MaxValue, 30000)]
    [InlineData(-30000, -30000)]
    [InlineData(30000, 30000)]
    public void AC5_clamp(int input, int expected) => Assert.Equal(expected, PhysicalCoordinates.Clamp(input));
}
