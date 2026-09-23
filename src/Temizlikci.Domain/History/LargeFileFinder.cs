using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.History;

/// <summary>A large file somewhere in a scan, with its path of IDs from the scan root (for recycling and navigation).</summary>
public sealed record LargeFile(FileNode Node, string Path, IReadOnlyList<string> IdPath)
{
    public string Id => Path;
}

/// <summary>
/// Collects the largest individual files in a tree. The scanner keeps files of 10 MB or more as their own nodes, so
/// these are exactly the files worth listing.
/// </summary>
public static class LargeFileFinder
{
    public const int DefaultLimit = 200;

    public static IReadOnlyList<LargeFile> Largest(FileNode root, int limit = DefaultLimit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        // A bounded min-heap: the tree can hold hundreds of thousands of files, the list only needs the top ones.
        var heap = new PriorityQueue<LargeFile, long>();
        var ids = new List<string>();
        Visit(NodeRef.Root(root));
        var result = new List<LargeFile>(heap.Count);
        while (heap.Count > 0) result.Add(heap.Dequeue());
        result.Reverse();
        return result;

        void Visit(NodeRef folder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ids.Add(folder.Id);
            foreach (var child in folder.Children)
            {
                switch (child.Node.Kind)
                {
                    case NodeKind.File:
                        var file = new LargeFile(child.Node, child.Path, [.. ids, child.Id]);
                        if (heap.Count < limit)
                        {
                            heap.Enqueue(file, child.Node.AllocatedSize);
                        }
                        else if (limit > 0 && heap.TryPeek(out _, out long smallest) && child.Node.AllocatedSize > smallest)
                        {
                            heap.EnqueueDequeue(file, child.Node.AllocatedSize);
                        }
                        break;
                    case NodeKind.Directory:
                        Visit(child);
                        break;
                }
            }
            ids.RemoveAt(ids.Count - 1);
        }
    }
}
