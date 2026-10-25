using CanvasWm.Core.Frames;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Tests.FramesSpec;

public sealed class DisplayChoiceTests
{
    private static readonly Rect A = new(0, 0, 1920, 1080);
    private static readonly Rect B = new(1920, 0, 1280, 960);

    [Fact]
    public void AC3_largest_overlap_wins()
    {
        Assert.Equal(1, DisplayChoice.ForRect(new Rect(1930, 10, 600, 400), new[] { A, B }, 0));
        Assert.Equal(1, DisplayChoice.ForRect(new Rect(1800, 0, 400, 400), new[] { A, B }, -1));
    }

    [Fact]
    public void AC3_tie_lowest_index()
    {
        var frames = new[] { new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 1920, 1080) };
        Assert.Equal(0, DisplayChoice.ForRect(new Rect(1820, 0, 200, 100), frames, 1));
    }

    [Fact]
    public void AC3_tiny_overlap_beats_current()
    {
        Assert.Equal(0, DisplayChoice.ForRect(new Rect(1919, 1079, 705, 423), new[] { A, B }, 1));
    }

    [Fact]
    public void AC3_no_overlap_fallbacks()
    {
        var far = new Rect(5000, 5000, 10, 10);
        Assert.Equal(1, DisplayChoice.ForRect(far, new[] { A, B }, 1));
        Assert.Equal(0, DisplayChoice.ForRect(far, new[] { A, B }, -1));
        Assert.Equal(0, DisplayChoice.ForRect(far, new[] { A, B }, 7));
        Assert.Equal(0, DisplayChoice.ForRect(new Rect(1920, 1000, 10, 10), new[] { A }, -1));
    }

    [Fact]
    public void AC3_empty_list_is_minus_one()
    {
        Assert.Equal(-1, DisplayChoice.ForRect(new Rect(0, 0, 10, 10), Array.Empty<Rect>(), 0));
        Assert.Equal(-1, DisplayChoice.ForRect(new Rect(0, 0, 10, 10), Array.Empty<Rect>(), -1));
    }

    [Fact]
    public void AC3_nan_rect_uses_current()
    {
        Assert.Equal(1, DisplayChoice.ForRect(new Rect(double.NaN, 0, 10, 10), new[] { A, B }, 1));
    }

    [Fact]
    public void AC3_null_list_throws()
    {
        Assert.Throws<ArgumentNullException>(() => DisplayChoice.ForRect(new Rect(0, 0, 1, 1), null!, 0));
    }
}
