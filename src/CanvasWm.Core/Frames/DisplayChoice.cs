using CanvasWm.Core.Geometry;
using CanvasWm.Core.Parking;

namespace CanvasWm.Core.Frames;

/// <summary>Which display's coordinate mapping applies to a rect (B9). Pure.</summary>
public static class DisplayChoice
{
    /// <summary>Index into displayFrames of the frame with the largest positive
    /// ParkGeometry.IntersectionArea with rect (ties: lowest index). If none intersects:
    /// currentIndex when 0 &lt;= currentIndex &lt; Count, else 0 when the list is non-empty,
    /// else -1.</summary>
    /// <param name="rect">The rect, in DIPs.</param>
    /// <param name="displayFrames">The displays' full frames, in DIPs.</param>
    /// <param name="currentIndex">The display the window is on now, or -1 if unknown.</param>
    /// <returns>The display index, or -1.</returns>
    /// <exception cref="ArgumentNullException">displayFrames is null.</exception>
    public static int ForRect(Rect rect, IReadOnlyList<Rect> displayFrames, int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(displayFrames);
        int best = -1;
        double bestArea = 0.0;
        for (int i = 0; i < displayFrames.Count; i++)
        {
            double area = ParkGeometry.IntersectionArea(rect, displayFrames[i]);
            if (area > bestArea)
            {
                bestArea = area;
                best = i;
            }
        }

        if (best >= 0)
        {
            return best;
        }

        if (currentIndex >= 0 && currentIndex < displayFrames.Count)
        {
            return currentIndex;
        }

        return displayFrames.Count > 0 ? 0 : -1;
    }
}
