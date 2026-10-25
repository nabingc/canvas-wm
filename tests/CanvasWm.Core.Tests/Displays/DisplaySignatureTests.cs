using System.Globalization;
using CanvasWm.Core.Displays;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Tests.DisplaysSpec;

public sealed class DisplaySignatureTests
{
    private static DisplaySignatureEntry E(string k, double x, double y, double w, double h, int dpi) =>
        new(k, new Rect(x, y, w, h), dpi);

    [Fact]
    public void AC1_SingleEntry()
    {
        Assert.Equal("A@0,0,1920,1080@96", DisplaySignature.Of(new[] { E("A", 0, 0, 1920, 1080, 96) }));
    }

    [Fact]
    public void AC1_SortedAndInvariantCulture()
    {
        var input = new[] { E("B", 1920, 0, 1706.6666666666667, 960, 144), E("A", 0, 0, 1920, 1080, 96) };
        const string expected = "A@0,0,1920,1080@96;B@1920,0,1706.6666666666667,960@144";
        Assert.Equal(expected, DisplaySignature.Of(input));
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(expected, DisplaySignature.Of(input));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    [Trait("Assumption", "A4")]
    public void AC1_OrdinalSortIsCaseSensitive()
    {
        // ordinal: uppercase before lowercase
        var s = DisplaySignature.Of(new[] { E("a", 0, 0, 1, 1, 96), E("B", 0, 0, 1, 1, 96) });
        Assert.StartsWith("B@", s);
    }

    [Fact]
    public void AC1_EmptyNullAndBadKey()
    {
        Assert.Equal("", DisplaySignature.Of(Array.Empty<DisplaySignatureEntry>()));
        Assert.Throws<ArgumentNullException>(() => DisplaySignature.Of(null!));
        Assert.Throws<ArgumentException>(() => DisplaySignature.Of(new[] { E("", 0, 0, 1, 1, 96) }));
        Assert.Throws<ArgumentException>(() => DisplaySignature.Of(new[] { E(null!, 0, 0, 1, 1, 96) }));
    }
}
