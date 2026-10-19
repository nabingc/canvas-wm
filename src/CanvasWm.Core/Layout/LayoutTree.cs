namespace CanvasWm.Core.Layout;

/// <summary>Tree helpers.</summary>
public static class LayoutTree
{
    /// <summary>First DesktopNode in depth-first pre-order (children in order), or null.
    /// Null root gives null.</summary>
    /// <param name="root">Tree root.</param>
    public static DesktopNode? FindDesktop(LayoutNode? root)
    {
        switch (root)
        {
            case DesktopNode d:
                return d;
            case SplitNode s:
                foreach (var c in s.Children)
                {
                    var found = FindDesktop(c);
                    if (found is not null)
                    {
                        return found;
                    }
                }
                return null;
            default:
                return null;
        }
    }
}
