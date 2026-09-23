using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.History;

public enum GrowthKind
{
    Grew,
    Shrank,
    /// <summary>Recorded now but not before: new, or under the recording threshold last time.</summary>
    Appeared,
    /// <summary>Recorded before but gone from the tree now.</summary>
    Removed,
}

public sealed record GrowthChange(string Path, long? Previous, long? Current)
{
    public long Delta => (Current ?? 0) - (Previous ?? 0);

    public GrowthKind Kind => (Previous, Current) switch
    {
        (null, _) => GrowthKind.Appeared,
        (_, null) => GrowthKind.Removed,
        _ => Delta >= 0 ? GrowthKind.Grew : GrowthKind.Shrank,
    };

    public string Name => NodePath.Name(Path);
}

/// <summary>What changed between two scans of the same location.</summary>
public sealed class GrowthReport
{
    /// <summary>Changes smaller than this are noise (caches ticking, logs rotating).</summary>
    public const long MinimumChange = 10_000_000;

    public GrowthReport(DateTime previousDateUtc, DateTime currentDateUtc, IReadOnlyDictionary<string, GrowthChange> changes)
    {
        PreviousDateUtc = previousDateUtc;
        CurrentDateUtc = currentDateUtc;
        Changes = changes;
    }

    public DateTime PreviousDateUtc { get; }

    public DateTime CurrentDateUtc { get; }

    public IReadOnlyDictionary<string, GrowthChange> Changes { get; }

    public GrowthChange? ChangeFor(string path) => Changes.GetValueOrDefault(NodePath.Trim(path));

    /// <summary>The largest changes, leaving out folders whose change is mostly explained by one item inside them, so
    /// the list points at where growth actually happened.</summary>
    public IReadOnlyList<GrowthChange> BiggestChanges(int limit = 50)
    {
        var largestChildDelta = new Dictionary<string, long>(NodePath.Comparer);
        foreach (var change in Changes.Values)
        {
            string? parent = NodePath.Parent(change.Path);
            if (parent is null) continue;
            long existing = largestChildDelta.GetValueOrDefault(parent);
            if (Math.Abs(change.Delta) > Math.Abs(existing)) largestChildDelta[parent] = change.Delta;
        }
        return Changes.Values
            .Where(change =>
            {
                if (!largestChildDelta.TryGetValue(change.Path, out long child)) return true;
                bool explained = Math.Sign(child) == Math.Sign(change.Delta) && Math.Abs(child) * 10 >= Math.Abs(change.Delta) * 8;
                return !explained;
            })
            .OrderByDescending(change => Math.Abs(change.Delta))
            .ThenBy(change => change.Path, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    public static GrowthReport Compare(ScanSnapshot previous, ScanSnapshot current, long minimumChange = MinimumChange)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var unread = new HashSet<string>(previous.UnreadPaths, NodePath.Comparer);
        unread.UnionWith(current.UnreadPaths);
        bool IsComparable(string path) => !unread.Contains(path) && !unread.Any(folder => NodePath.IsWithin(path, folder));

        var changes = new Dictionary<string, GrowthChange>(NodePath.Comparer);
        var paths = new HashSet<string>(previous.Sizes.Keys, NodePath.Comparer);
        paths.UnionWith(current.Sizes.Keys);
        foreach (var path in paths)
        {
            if (!IsComparable(path)) continue;
            long? before = previous.Sizes.TryGetValue(path, out long p) ? p : null;
            long? after = current.Sizes.TryGetValue(path, out long c) ? c : null;
            var change = new GrowthChange(path, before, after);
            if (Math.Abs(change.Delta) >= minimumChange) changes[path] = change;
        }
        return new GrowthReport(previous.DateUtc, current.DateUtc, changes);
    }
}
