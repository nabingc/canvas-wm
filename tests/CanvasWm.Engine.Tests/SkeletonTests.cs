using System.Reflection;

namespace CanvasWm.Engine.Tests;

public sealed class SkeletonTests
{
    [Fact]
    public void ProductAssemblyLoads()
    {
        var assembly = Assembly.Load("CanvasWm.Engine");
        Assert.Equal("CanvasWm.Engine", assembly.GetName().Name);
    }

    [Fact]
    public void EngineReferencesOnlyCore()
    {
        var refs = Assembly.Load("CanvasWm.Engine").GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("CanvasWm.", StringComparison.Ordinal));
        Assert.Subset(new HashSet<string> { "CanvasWm.Core" }, new HashSet<string>(refs));
    }
}
