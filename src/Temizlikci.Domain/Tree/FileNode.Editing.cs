namespace Temizlikci.Domain.Tree;

/// <summary>A node together with its path — what view models hold when they need to act on an item.</summary>
public readonly record struct NodeRef(FileNode Node, string Path)
{
    public string Id => FileNode.IdOf(Node, Path);

    public NodeRef Child(FileNode child) => new(child, FileNode.ChildPath(Path, child));

    public IEnumerable<NodeRef> Children => Node.Children.Select(Child);

    /// <summary>A scan's root: its name is its path.</summary>
    public static NodeRef Root(FileNode root) => new(root, root?.Name ?? throw new ArgumentNullException(nameof(root)));
}

/// <summary>
/// Copy-on-write edits of an immutable tree. Paths of IDs always start with the root's ID (its path) and end with the
/// target; every folder on the way is rebuilt and re-totalled, everything else is shared.
/// </summary>
public static class TreeEditing
{
    /// <summary>The root without the node at the end of <paramref name="idPath"/>. <c>null</c> if the path doesn't exist
    /// or names the root itself.</summary>
    public static FileNode? RemovingDescendant(this FileNode root, IReadOnlyList<string> idPath)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(idPath);
        if (idPath.Count < 2 || !string.Equals(idPath[0], root.Name, StringComparison.Ordinal)) return null;
        return Remove(root, root.Name, idPath, 1);
    }

    private static FileNode? Remove(FileNode folder, string folderPath, IReadOnlyList<string> idPath, int index)
    {
        if (folder.Kind != NodeKind.Directory) return null;
        int position = IndexOfChild(folder, folderPath, idPath[index]);
        if (position < 0) return null;
        var children = folder.Children.ToList();
        if (index == idPath.Count - 1)
        {
            children.RemoveAt(position);
        }
        else
        {
            var child = children[position];
            var updated = Remove(child, FileNode.ChildPath(folderPath, child), idPath, index + 1);
            if (updated is null) return null;
            children[position] = updated;
        }
        return folder.WithChildren(children);
    }

    /// <summary>The root with <paramref name="node"/> added inside the folder at the end of <paramref name="folderIdPath"/>
    /// (which starts with the root's ID; the root alone means "directly in the root"). <c>null</c> if the path doesn't exist.</summary>
    public static FileNode? InsertingDescendant(this FileNode root, FileNode node, IReadOnlyList<string> folderIdPath)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(folderIdPath);
        if (folderIdPath.Count < 1 || !string.Equals(folderIdPath[0], root.Name, StringComparison.Ordinal)) return null;
        return Insert(root, root.Name, node, folderIdPath, 1);
    }

    private static FileNode? Insert(FileNode folder, string folderPath, FileNode node, IReadOnlyList<string> idPath, int index)
    {
        if (folder.Kind != NodeKind.Directory) return null;
        if (index == idPath.Count) return folder.Adding(node);
        int position = IndexOfChild(folder, folderPath, idPath[index]);
        if (position < 0) return null;
        var children = folder.Children.ToList();
        var child = children[position];
        var updated = Insert(child, FileNode.ChildPath(folderPath, child), node, idPath, index + 1);
        if (updated is null) return null;
        children[position] = updated;
        return folder.WithChildren(children);
    }

    /// <summary>Follows <paramref name="ids"/> (starting with the root's ID) and returns the nodes found, with their paths,
    /// stopping at the first missing one.</summary>
    public static IReadOnlyList<NodeRef> NodesAlong(this FileNode root, IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(ids);
        var result = new List<NodeRef>();
        if (ids.Count == 0 || !string.Equals(ids[0], root.Name, StringComparison.Ordinal)) return result;
        var current = NodeRef.Root(root);
        result.Add(current);
        for (int index = 1; index < ids.Count; index++)
        {
            int position = IndexOfChild(current.Node, current.Path, ids[index]);
            if (position < 0) break;
            current = current.Child(current.Node.Children[position]);
            result.Add(current);
        }
        return result;
    }

    /// <summary>The chain of nodes from the root down to the item at <paramref name="path"/>, or <c>null</c>.</summary>
    public static IReadOnlyList<NodeRef>? Locate(this FileNode root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        string target = NodePath.Trim(path);
        var chain = new List<NodeRef> { NodeRef.Root(root) };
        while (!NodePath.Comparer.Equals(NodePath.Trim(chain[^1].Path), target))
        {
            var current = chain[^1];
            NodeRef? next = null;
            foreach (var child in current.Children)
            {
                if (!child.Node.IsItem) continue;
                if (NodePath.IsSameOrWithin(target, child.Path))
                {
                    next = child;
                    break;
                }
            }
            if (next is null) return null;
            chain.Add(next.Value);
        }
        return chain;
    }

    private static int IndexOfChild(FileNode folder, string folderPath, string id)
    {
        var children = folder.Children;
        for (int position = 0; position < children.Count; position++)
        {
            if (string.Equals(FileNode.ChildId(folderPath, children[position]), id, StringComparison.Ordinal)) return position;
        }
        return -1;
    }
}
