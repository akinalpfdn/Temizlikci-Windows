using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.History;

/// <summary>A compact record of one scan, kept so later scans can show what grew. Only sizes by path; no file contents.</summary>
/// <param name="Sizes">Allocated bytes by item path.</param>
/// <param name="UnreadPaths">Folders that couldn't be read in this scan; sizes at or below them aren't comparable.</param>
public sealed record ScanSnapshot(int Version, string LocationPath, DateTime DateUtc, IReadOnlyDictionary<string, long> Sizes, IReadOnlySet<string> UnreadPaths)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Chooses which items a snapshot records: everything near the top, anything large, every developer artifact, and every
/// path the previous snapshot recorded (so a missing path really means the item is gone, not that it shrank below the
/// recording threshold).
/// </summary>
public static class SnapshotBuilder
{
    public const int AlwaysRecordedDepth = 2;
    public const long MinimumRecordedSize = 100_000_000;

    public static ScanSnapshot Snapshot(FileNode root, string locationPath, DateTime dateUtc, IReadOnlySet<string> alsoRecording)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(alsoRecording);
        var ancestorsOfExtras = new HashSet<string>(NodePath.Comparer);
        foreach (var path in alsoRecording)
        {
            var parent = NodePath.Parent(path);
            while (parent is not null && ancestorsOfExtras.Add(parent)) parent = NodePath.Parent(parent);
        }
        var extras = new HashSet<string>(alsoRecording, NodePath.Comparer);

        var sizes = new Dictionary<string, long>(NodePath.Comparer);
        var unread = new HashSet<string>(NodePath.Comparer);
        Visit(NodeRef.Root(root), 0);
        return new ScanSnapshot(ScanSnapshot.CurrentVersion, locationPath, dateUtc, sizes, unread);

        void Visit(NodeRef item, int depth)
        {
            var node = item.Node;
            if (node.Kind == NodeKind.Inaccessible)
            {
                unread.Add(NodePath.Trim(item.Path));
                return;
            }
            if (node.Kind is not (NodeKind.Directory or NodeKind.File)) return;
            string path = NodePath.Trim(item.Path);
            bool isLarge = node.AllocatedSize >= MinimumRecordedSize;
            if (depth <= AlwaysRecordedDepth || isLarge || extras.Contains(path)) sizes[path] = node.AllocatedSize;
            if (node.Kind != NodeKind.Directory || !(depth < AlwaysRecordedDepth || isLarge || ancestorsOfExtras.Contains(path))) return;
            foreach (var child in item.Children) Visit(child, depth + 1);
        }
    }
}

/// <summary>Keeps scan snapshots per location.</summary>
public interface ISnapshotStore
{
    /// <summary>The newest snapshots of a location, newest first.</summary>
    IReadOnlyList<ScanSnapshot> Recent(string locationPath, int limit);

    void Save(ScanSnapshot snapshot);

    /// <summary>Deletes all but the newest <paramref name="count"/> snapshots of a location.</summary>
    void Prune(string locationPath, int count);
}

public static class SnapshotStoreExtensions
{
    public static ScanSnapshot? Latest(this ISnapshotStore store, string locationPath)
    {
        ArgumentNullException.ThrowIfNull(store);
        var recent = store.Recent(locationPath, 1);
        return recent.Count > 0 ? recent[0] : null;
    }
}
