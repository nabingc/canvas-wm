using System.Reflection;

namespace CanvasWm.Win32.Tests;

public sealed class SkeletonTests
{
    [Fact]
    public void ProductAssemblyLoads()
    {
        var assembly = Assembly.Load("CanvasWm.Win32");
        Assert.Equal("CanvasWm.Win32", assembly.GetName().Name);
    }
}
