namespace Temizlikci.Domain.Tree;

public enum NodeKind : byte
{
    Directory,
    File,
    /// <summary>Files below the individual-file threshold, summed per directory.</summary>
    SmallerFiles,
    /// <summary>A folder the scanner could not read, typically because it needs administrator rights.</summary>
    Inaccessible,
    /// <summary>Used space on the volume that no scanned folder accounts for. Only in whole-volume results.</summary>
    Unattributed,
    /// <summary>Space not measured yet while a scan is running.</summary>
    Pending,
}

/// <summary>
/// One entry in a scan result. Directories keep their full structure; individual files are kept only above
/// <see cref="Scanning.ScanConfiguration.IndividualFileThreshold"/>, and the rest of a directory's files are folded
/// into one <see cref="NodeKind.SmallerFiles"/> child, so multi-million-file volumes stay affordable in memory.
/// </summary>
/// <remarks>
/// Immutable, so a finished tree can be read from background analyses while the UI edits a copy. A node stores its
/// name only — never its path — because a second copy of every path was the macOS app's largest memory cost. The root's
/// name is the scanned location's full path; every other path is built from the parent's while walking. A node's ID is
/// its full path, or its folder's path plus a suffix for the aggregate kinds that stand for their folder.
/// </remarks>
public sealed class FileNode
{
    private static readonly FileNode[] NoChildren = [];

    private readonly FileNode[] children;
    private readonly long modifiedTicks;

    private FileNode(string name, NodeKind kind, long allocatedSize, int fileCount, DateTime? modifiedUtc, FileNode[] children)
    {
        Name = name;
        Kind = kind;
        AllocatedSize = allocatedSize;
        FileCount = fileCount;
        modifiedTicks = modifiedUtc?.ToUniversalTime().Ticks ?? 0;
        this.children = children;
    }

    /// <summary>The item's name; the full path for a scan's root; empty for aggregate kinds.</summary>
    public string Name { get; }

    public NodeKind Kind { get; }

    /// <summary>Space on disk in bytes (allocation size, not length). Hard links count once.</summary>
    public long AllocatedSize { get; }

    /// <summary>Number of files at or below this node.</summary>
    public int FileCount { get; }

    public DateTime? ModifiedUtc => modifiedTicks == 0 ? null : new DateTime(modifiedTicks, DateTimeKind.Utc);

    /// <summary>Sorted by <see cref="AllocatedSize"/>, largest first.</summary>
    public IReadOnlyList<FileNode> Children => children;

    public bool IsContainer => Kind == NodeKind.Directory;

    /// <summary>True for nodes that describe their folder rather than an item of their own.</summary>
    public bool StandsForFolder => Kind is NodeKind.SmallerFiles or NodeKind.Unattributed or NodeKind.Pending;

    /// <summary>True for real files and folders — the things that exist on disk under their own name.</summary>
    public bool IsItem => Kind is NodeKind.Directory or NodeKind.File or NodeKind.Inaccessible;

    // MARK: Paths and IDs

    /// <summary>The path of a child, given this node's path. Aggregates share their folder's path.</summary>
    public static string ChildPath(string parentPath, FileNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return child.StandsForFolder ? parentPath : NodePath.Join(parentPath, child.Name);
    }

    /// <summary>A node's ID from its own path: the path itself, or the path plus a suffix for aggregates.</summary>
    public static string IdOf(FileNode node, string path)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Kind switch
        {
            NodeKind.SmallerFiles => path + "\0smaller-files",
            NodeKind.Unattributed => path + "\0unattributed",
            NodeKind.Pending => path + "\0pending",
            _ => path,
        };
    }

    /// <summary>The ID of a child, given this node's path.</summary>
    public static string ChildId(string parentPath, FileNode child) => IdOf(child, ChildPath(parentPath, child));

    // MARK: Factories

    public static FileNode File(string name, long allocatedSize, DateTime? modifiedUtc) =>
        new(name, NodeKind.File, allocatedSize, 1, modifiedUtc, NoChildren);

    public static FileNode SmallerFiles(int count, long allocatedSize) =>
        new(string.Empty, NodeKind.SmallerFiles, allocatedSize, count, null, NoChildren);

    public static FileNode Inaccessible(string name) =>
        new(name, NodeKind.Inaccessible, 0, 0, null, NoChildren);

    public static FileNode Unattributed(long allocatedSize) =>
        new(string.Empty, NodeKind.Unattributed, allocatedSize, 0, null, NoChildren);

    public static FileNode Pending(long allocatedSize) =>
        new(string.Empty, NodeKind.Pending, allocatedSize, 0, null, NoChildren);

    /// <summary>A directory with its children sorted largest first and its totals summed.</summary>
    public static FileNode Directory(string name, DateTime? modifiedUtc, IEnumerable<FileNode> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        var sorted = children.ToArray();
        return Directory(name, modifiedUtc, sorted, sortedInPlace: false);
    }

    /// <summary>
    /// A directory whose total is already known, without children, for folders still being measured during a scan.
    /// </summary>
    public static FileNode Measuring(string name, long allocatedSize) =>
        new(name, NodeKind.Directory, allocatedSize, 0, null, NoChildren);

    /// <summary>Builds a node exactly as stored, for decoding archives. Children must already be sorted.</summary>
    internal static FileNode Restore(string name, NodeKind kind, long allocatedSize, int fileCount, DateTime? modifiedUtc, FileNode[] children) =>
        new(name, kind, allocatedSize, fileCount, modifiedUtc, children.Length == 0 ? NoChildren : children);

    private static FileNode Directory(string name, DateTime? modifiedUtc, FileNode[] children, bool sortedInPlace)
    {
        if (!sortedInPlace && children.Length > 1)
        {
            // Stable, so equally sized items keep the order the scanner listed them in.
            var ordered = children.OrderByDescending(child => child.AllocatedSize).ToArray();
            children = ordered;
        }
        long size = 0;
        long files = 0;
        foreach (var child in children)
        {
            size += child.AllocatedSize;
            files += child.FileCount;
        }
        return new FileNode(name, NodeKind.Directory, size, (int)Math.Min(files, int.MaxValue), modifiedUtc,
            children.Length == 0 ? NoChildren : children);
    }

    /// <summary>A copy of this directory with one more child, re-sorted and re-totalled.</summary>
    public FileNode Adding(FileNode child) => Directory(Name, ModifiedUtc, [.. children, child], sortedInPlace: false);

    /// <summary>A copy of this directory with different children, re-sorted and re-totalled.</summary>
    public FileNode WithChildren(IEnumerable<FileNode> newChildren) => Directory(Name, ModifiedUtc, newChildren);

    /// <summary>A copy of this node under another name, for turning a subtree into a scan's root.</summary>
    public FileNode Renamed(string name) => new(name, Kind, AllocatedSize, FileCount, ModifiedUtc, children);

    public override string ToString() => $"{Kind} {Name} ({AllocatedSize} bytes)";
}
