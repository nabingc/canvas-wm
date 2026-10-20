using System.Reflection;
using CanvasWm.Core;
using CanvasWm.Core.Parking;

namespace CanvasWm.Engine.Tests.TuningSep;

public sealed class TuningPolicySeparationTests
{
    private static HashSet<string> Names(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).Where(n => n != "EqualityContract").ToHashSet();

    [Fact]
    public void AC10_Tuning_names_disjoint_from_policy_and_park_options()
    {
        var tuning = Names(typeof(Tuning));
        Assert.Equal(50, tuning.Count);
        Assert.Empty(tuning.Intersect(Names(typeof(EnginePolicy))));
        Assert.Empty(tuning.Intersect(Names(typeof(ParkOptions))));
    }

    [Fact]
    public void AC10_Policy_values_unchanged()
    {
        Assert.Equal(0.15, EnginePolicy.Default.DropHold);
        Assert.Equal(0.2, EnginePolicy.Default.DesktopEdge);
        Assert.Equal(0.25, EnginePolicy.Default.DimOpacity);
    }
}
