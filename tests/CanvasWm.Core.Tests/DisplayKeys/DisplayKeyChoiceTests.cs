using CanvasWm.Core.Displays;

namespace CanvasWm.Core.Tests.DisplayKeysSpec;

public sealed class DisplayKeyChoiceTests
{
    private static IReadOnlyList<string> Run(string?[] monitors, params (string? S, string? P)[] paths) =>
        DisplayKeyChoice.Assign(monitors, paths.Select(p => new DisplayPathName(p.S, p.P)).ToArray());

    [Fact]
    public void AC1_CaseInsensitiveSourceMatch()
    {
        Assert.Equal(new[] { "P1", "P2" },
            Run(new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" }, (@"\\.\DISPLAY2", "P2"), (@"\\.\display1", "P1")));
    }

    [Fact]
    public void AC1_CloneFirstPathWins()
    {
        Assert.Equal(new[] { "PA" }, Run(new[] { @"\\.\DISPLAY1" }, (@"\\.\DISPLAY1", "PA"), (@"\\.\DISPLAY1", "PB")));
    }

    [Fact]
    public void AC1_EmptyDevicePathSkippedThenNextOrSource()
    {
        Assert.Equal(new[] { "PB" }, Run(new[] { @"\\.\DISPLAY1" }, (@"\\.\DISPLAY1", ""), (@"\\.\DISPLAY1", "PB")));
        Assert.Equal(new[] { "PB" }, Run(new[] { @"\\.\DISPLAY1" }, (@"\\.\DISPLAY1", null), (@"\\.\DISPLAY1", "PB")));
        Assert.Equal(new[] { @"\\.\DISPLAY1" }, Run(new[] { @"\\.\DISPLAY1" }, (@"\\.\DISPLAY1", "")));
    }

    [Fact]
    public void AC1_FallbackToSourceNameThenIndex()
    {
        Assert.Equal(new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" }, Run(new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" }));
        Assert.Equal(new[] { "display-0", "display-1" }, Run(new string?[] { null, "" }));
    }

    [Fact]
    public void AC1_DuplicatesGetSuffix()
    {
        Assert.Equal(new[] { "P", "P#2" }, Run(new[] { "a", "b" }, ("a", "P"), ("b", "P")));
        Assert.Equal(new[] { "P", "P#2", "P#3" }, Run(new[] { "a", "b", "c" }, ("a", "P"), ("b", "P"), ("c", "P")));
        Assert.Equal(new[] { "P", "P#2", "P#3" }, Run(new[] { "a", "b", "c" }, ("a", "P"), ("b", "P#2"), ("c", "P")));
    }

    [Fact]
    public void AC1_EmptyAndNulls()
    {
        Assert.Empty(Run(Array.Empty<string?>()));
        Assert.Throws<ArgumentNullException>(() => DisplayKeyChoice.Assign(null!, Array.Empty<DisplayPathName>()));
        Assert.Throws<ArgumentNullException>(() => DisplayKeyChoice.Assign(Array.Empty<string?>(), null!));
    }
}
