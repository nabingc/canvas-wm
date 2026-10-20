using System.Reflection;
using CanvasWm.Core.Shortcuts;

namespace CanvasWm.Core.Tests.ShortcutsSpec;

public sealed class ArchitectureTests
{
    private static readonly string[] Listed =
    {
        "Key","Modifiers","KeyCombo","ShortcutRules","ShortcutAction","CommandKind","TargetEngine","ShortcutCommand",
        "ShortcutActionInfo","ViewSet","ShortcutCatalog","ShortcutBindings","ShortcutProfile","ShortcutProfiles",
        "ReservedKind","ReservedCombo","ReservedCombos","DispatchEntry","KeyVerdict","KeyDownOutcome","DispatchState",
        "ShortcutDispatch","EditOutcome","EditResult","ShortcutEditor","ShortcutStore",
    };

    [Fact]
    public void AC30_Core_references_no_canvaswm_assembly()
    {
        var refs = typeof(KeyCombo).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "").Where(n => n.StartsWith("CanvasWm.", StringComparison.Ordinal));
        Assert.Empty(refs);
    }

    [Fact]
    public void AC30_Listed_types_are_public_in_namespace_and_assembly()
    {
        var asm = typeof(KeyCombo).Assembly;
        Assert.Equal("CanvasWm.Core", asm.GetName().Name);
        foreach (var name in Listed)
        {
            var t = asm.GetType("CanvasWm.Core.Shortcuts." + name);
            Assert.True(t is not null && t.IsPublic, name);
        }
    }

    private static IEnumerable<Type> Leaves(Type t)
    {
        if (t.IsByRef || t.IsArray) { foreach (var x in Leaves(t.GetElementType()!)) yield return x; yield break; }
        if (t.IsGenericType)
        {
            yield return t.GetGenericTypeDefinition();
            foreach (var a in t.GetGenericArguments()) foreach (var x in Leaves(a)) yield return x;
            yield break;
        }
        yield return t;
    }

    [Fact]
    public void AC30_Public_surface_uses_only_System_and_Core_types()
    {
        var asm = typeof(KeyCombo).Assembly;
        var types = asm.GetExportedTypes().Where(t => t.Namespace == "CanvasWm.Core.Shortcuts").ToList();
        Assert.NotEmpty(types);
        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var t in types)
        {
            var used = new List<Type>();
            foreach (var m in t.GetMembers(F))
            {
                switch (m)
                {
                    case MethodBase mb:
                        foreach (var p in mb.GetParameters()) used.Add(p.ParameterType);
                        if (mb is MethodInfo mi) used.Add(mi.ReturnType);
                        break;
                    case PropertyInfo pi: used.Add(pi.PropertyType); break;
                    case FieldInfo fi: used.Add(fi.FieldType); break;
                }
            }
            foreach (var u in used.SelectMany(Leaves))
            {
                string ns = u.Namespace ?? "";
                Assert.True(ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)
                            || ns == "CanvasWm.Core" || ns.StartsWith("CanvasWm.Core.", StringComparison.Ordinal),
                    $"{t.Name} exposes {u.FullName}");
            }
        }
    }
}
