using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.Layout;

public enum FillRole
{
    /// <summary>A palette slot and the ring depth, which lightens the slot's tint.</summary>
    Slot,
    /// <summary>Smaller items, merged slivers, and top-level entries beyond the last slot.</summary>
    Neutral,
    /// <summary>Space no folder accounts for.</summary>
    UnattributedHatch,
    /// <summary>Folders that couldn't be read.</summary>
    InaccessibleHatch,
    /// <summary>Not measured yet.</summary>
    Pending,
    /// <summary>Highlight Reclaimable: a matched item (or something inside one) with its cleanup label.</summary>
    Safety,
    /// <summary>Highlight Reclaimable: everything else, faded.</summary>
    Dimmed,
}

/// <summary>How one segment of the sunburst is colored. Resolved to real colors by the view.</summary>
public readonly record struct SegmentFill(FillRole Role, int Slot = 0, int Depth = 0, SafetyLevel Safety = SafetyLevel.Safe)
{
    public static SegmentFill ForSlot(int slot, int depth) => new(FillRole.Slot, slot, depth);
    public static SegmentFill Neutral { get; } = new(FillRole.Neutral);
    public static SegmentFill UnattributedHatch { get; } = new(FillRole.UnattributedHatch);
    public static SegmentFill InaccessibleHatch { get; } = new(FillRole.InaccessibleHatch);
    public static SegmentFill Pending { get; } = new(FillRole.Pending);
    public static SegmentFill Dimmed { get; } = new(FillRole.Dimmed);
    public static SegmentFill ForSafety(SafetyLevel level) => new(FillRole.Safety, Safety: level);

    public bool IsHatched => Role is FillRole.UnattributedHatch or FillRole.InaccessibleHatch;
}

/// <summary>One drawn ring slice.</summary>
/// <param name="NodeId">The node behind the slice; <c>null</c> for merged slivers.</param>
/// <param name="Depth">1 = innermost ring.</param>
/// <param name="StartAngle">Radians, clockwise from 12 o'clock.</param>
public sealed record SunburstSegment(
    string Id,
    string? NodeId,
    string Name,
    long AllocatedSize,
    int Depth,
    double StartAngle,
    double EndAngle,
    SegmentFill Fill,
    bool IsMerged)
{
    public double MidAngle => (StartAngle + EndAngle) / 2;
    public double Sweep => EndAngle - StartAngle;
}

/// <summary>Pure geometry for the sunburst: which segments to draw and where. No UI types, so it's unit-tested.</summary>
public static class SunburstLayout
{
    public const int RingCount = 3;
    public const int SlotCount = 8;

    /// <summary>Segments thinner than this are merged per parent so every drawn segment stays clickable.</summary>
    public static readonly double MinimumSweep = 1.2 * Math.PI / 180;

    public const double HoleFraction = 0.41;

    /// <summary>The segments for <paramref name="folder"/>, whose path is <paramref name="folderPath"/>.</summary>
    public static IReadOnlyList<SunburstSegment> Segments(FileNode folder, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var output = new List<SunburstSegment>();
        if (folder.AllocatedSize <= 0) return output;
        var slots = SlotAssignments(folder, folderPath);
        LayOut(new NodeRef(folder, folderPath), 0, 2 * Math.PI, 1, inheritedSlot: null, slots, output);
        return output;
    }

    /// <summary>Palette slots go to the folder's top-level folders and files in size order; special kinds and anything
    /// past the eighth slot stay neutral.</summary>
    public static IReadOnlyDictionary<string, int> SlotAssignments(FileNode folder, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var assignments = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in folder.Children)
        {
            if (!TakesSlot(child)) continue;
            if (assignments.Count >= SlotCount) break;
            assignments[FileNode.ChildId(folderPath, child)] = assignments.Count;
        }
        return assignments;
    }

    private static bool TakesSlot(FileNode node) =>
        node.Kind is NodeKind.Directory or NodeKind.File && node.AllocatedSize > 0;

    private static void LayOut(NodeRef parent, double start, double end, int depth, int? inheritedSlot,
        IReadOnlyDictionary<string, int> slots, List<SunburstSegment> output)
    {
        if (depth > RingCount || parent.Node.AllocatedSize <= 0) return;
        double span = end - start;
        double cursor = start;
        long mergedSize = 0;
        double mergedSweep = 0;

        // Children are kept sorted largest-first by FileNode.Directory.
        foreach (var child in parent.Children)
        {
            if (child.Node.AllocatedSize <= 0) continue;
            double sweep = span * child.Node.AllocatedSize / parent.Node.AllocatedSize;
            if (sweep < MinimumSweep)
            {
                mergedSize += child.Node.AllocatedSize;
                mergedSweep += sweep;
                continue;
            }
            string id = child.Id;
            int? slot = depth == 1 ? (slots.TryGetValue(id, out int assigned) ? assigned : null) : inheritedSlot;
            output.Add(new SunburstSegment(id, id, child.Node.Name, child.Node.AllocatedSize, depth, cursor, cursor + sweep,
                FillFor(child.Node, slot, depth), IsMerged: false));
            if (child.Node.Kind == NodeKind.Directory)
            {
                LayOut(child, cursor, cursor + sweep, depth + 1, slot, slots, output);
            }
            cursor += sweep;
        }

        if (mergedSweep > 0)
        {
            output.Add(new SunburstSegment(parent.Id + "\0merged", null, string.Empty, mergedSize, depth, cursor,
                cursor + mergedSweep, SegmentFill.Neutral, IsMerged: true));
        }
    }

    private static SegmentFill FillFor(FileNode node, int? slot, int depth) => node.Kind switch
    {
        NodeKind.Unattributed => SegmentFill.UnattributedHatch,
        NodeKind.Inaccessible => SegmentFill.InaccessibleHatch,
        NodeKind.Pending => SegmentFill.Pending,
        NodeKind.SmallerFiles => SegmentFill.Neutral,
        _ => slot is { } value ? SegmentFill.ForSlot(value, depth) : SegmentFill.Neutral,
    };

    // MARK: Geometry

    /// <summary>Ring bounds as fractions of the chart radius, matching the approved design.</summary>
    public static (double Inner, double Outer) RingBounds(int depth) => depth switch
    {
        1 => (0.43, 0.68),
        2 => (0.695, 0.865),
        _ => (0.88, 0.99),
    };

    /// <summary>The segment under a point in a square chart of side <paramref name="side"/>, or <c>null</c> for the
    /// hole or outside.</summary>
    public static SunburstSegment? SegmentAt(double x, double y, double side, IReadOnlyList<SunburstSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        double radius = side / 2;
        if (radius <= 0) return null;
        double dx = x - radius;
        double dy = y - radius;
        double distance = Math.Sqrt(dx * dx + dy * dy) / radius;
        int depth = 0;
        for (int ring = 1; ring <= RingCount; ring++)
        {
            var (inner, outer) = RingBounds(ring);
            if (distance >= inner && distance <= outer)
            {
                depth = ring;
                break;
            }
        }
        if (depth == 0) return null;
        double angle = Math.Atan2(dx, -dy);
        if (angle < 0) angle += 2 * Math.PI;
        foreach (var segment in segments)
        {
            if (segment.Depth == depth && angle >= segment.StartAngle && angle < segment.EndAngle) return segment;
        }
        return null;
    }

    /// <summary>True when the point falls inside the center hole.</summary>
    public static bool IsInHole(double x, double y, double side)
    {
        double radius = side / 2;
        if (radius <= 0) return false;
        double dx = x - radius;
        double dy = y - radius;
        return Math.Sqrt(dx * dx + dy * dy) / radius < HoleFraction;
    }

    /// <summary>True when <paramref name="segment"/> is <paramref name="hovered"/> or lies inside it on an outer ring.</summary>
    public static bool IsWithin(SunburstSegment segment, SunburstSegment hovered)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(hovered);
        if (segment.Id == hovered.Id) return true;
        const double tolerance = 1e-9;
        return segment.Depth > hovered.Depth
            && segment.StartAngle >= hovered.StartAngle - tolerance
            && segment.EndAngle <= hovered.EndAngle + tolerance;
    }
}
