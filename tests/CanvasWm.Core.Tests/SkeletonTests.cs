using System.Reflection;

namespace CanvasWm.Core.Tests;

public sealed class SkeletonTests
{
    [Fact]
    public void ProductAssemblyLoads()
    {
        var assembly = Assembly.Load("CanvasWm.Core");
        Assert.Equal("CanvasWm.Core", assembly.GetName().Name);
    }

    [Fact]
    public void CoreReferencesNoCanvasWmAssembly()
    {
        var refs = Assembly.Load("CanvasWm.Core").GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("CanvasWm.", StringComparison.Ordinal));
        Assert.Empty(refs);
    }
}
