using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;

namespace Temizlikci.Services.Scanning.Mft;

/// <summary>Turns a filled <see cref="MftRecordTable"/> into the same kind of tree the directory scanner builds.</summary>
internal static class MftTreeBuilder
{
    /// <summary>NTFS keeps the root folder of every volume in record 5.</summary>
    public const int RootRecord = 5;

    public sealed record Built(FileNode Root, long FileCount, long DirectoryCount);

    /// <summary>
    /// The tree below <paramref name="startRecord"/>, named <paramref name="rootPath"/>. Hard links count once: a file is
    /// listed under the folder of its primary name only. Junctions, symbolic links and mount points are left out, as the
    /// directory scanner leaves them out.
    /// </summary>
    public static Built Build(MftRecordTable table, int startRecord, string rootPath, ScanConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(configuration);
        var (starts, children) = ChildIndex(table);
        long files = 0;
        long directories = 0;
        var root = BuildFolder(startRecord, rootPath, rootPath);
        return new Built(root, files, directories);

        FileNode BuildFolder(int record, string name, string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            directories++;
            var nodes = new List<FileNode>();
            int smallCount = 0;
            long smallSize = 0;
            for (int index = starts[record]; index < starts[record + 1]; index++)
            {
                int child = children[index];
                if (table.Has(child, MftRecordTable.NameSurrogate)) continue;
                if (table.Has(child, MftRecordTable.Directory))
                {
                    string childName = table.Names[child] ?? string.Empty;
                    string childPath = NodePath.Join(path, childName);
                    if (configuration.SkippedPaths.Count > 0 && configuration.SkippedPaths.Contains(childPath)) continue;
                    nodes.Add(BuildFolder(child, childName, childPath));
                    continue;
                }
                files++;
                long size = table.Allocated[child];
                if (size >= configuration.IndividualFileThreshold && table.Names[child] is { } fileName)
                {
                    nodes.Add(FileNode.File(fileName, size, Time(table.Modified[child])));
                }
                else
                {
                    smallCount++;
                    smallSize += size;
                }
            }
            if (smallCount > 0) nodes.Add(FileNode.SmallerFiles(smallCount, smallSize));
            return FileNode.Directory(name, Time(table.Modified[record]), nodes);
        }
    }

    /// <summary>Children of every folder in compressed-row form: <c>children[starts[r]..starts[r + 1]]</c>.</summary>
    private static (int[] Starts, int[] Children) ChildIndex(MftRecordTable table)
    {
        var starts = new int[table.Capacity + 1];
        for (int record = 0; record < table.Capacity; record++)
        {
            if (IsListed(table, record)) starts[table.Parents[record] + 1]++;
        }
        for (int index = 1; index < starts.Length; index++) starts[index] += starts[index - 1];
        var children = new int[starts[^1]];
        var cursor = (int[])starts.Clone();
        for (int record = 0; record < table.Capacity; record++)
        {
            if (IsListed(table, record)) children[cursor[table.Parents[record]]++] = record;
        }
        return (starts, children);
    }

    /// <summary>An item with a name in some folder. The root names itself as its own parent and is not its own child.</summary>
    private static bool IsListed(MftRecordTable table, int record) =>
        table.Has(record, MftRecordTable.InUse) && table.Has(record, MftRecordTable.Named)
        && table.Parents[record] >= 0 && table.Parents[record] < table.Capacity && table.Parents[record] != record;

    /// <summary>The record of the folder at <paramref name="path"/> on the volume, or <c>null</c> when it isn't there.</summary>
    public static int? RecordFor(MftRecordTable table, string volumeRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(table);
        string trimmed = NodePath.Trim(path);
        if (NodePath.Comparer.Equals(trimmed, NodePath.Trim(volumeRoot))) return RootRecord;
        string relative = trimmed[NodePath.Trim(volumeRoot).TrimEnd('\\').Length..].TrimStart('\\');
        var (starts, children) = ChildIndex(table);
        int current = RootRecord;
        foreach (var component in relative.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            int? next = null;
            for (int index = starts[current]; index < starts[current + 1]; index++)
            {
                int child = children[index];
                if (table.Has(child, MftRecordTable.Directory) && string.Equals(table.Names[child], component, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }
            if (next is null) return null;
            current = next.Value;
        }
        return current;
    }

    private static DateTime? Time(long fileTime) => fileTime <= 0 ? null : DateTime.FromFileTimeUtc(fileTime);
}
