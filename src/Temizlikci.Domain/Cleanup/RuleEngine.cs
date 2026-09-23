using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.Cleanup;

/// <summary>An item recognized by a cleanup rule.</summary>
/// <param name="IdPath">IDs from the scan's root down to the matched node.</param>
public sealed record CleanupMatch(CleanupRule Rule, FileNode Node, string Path, IReadOnlyList<string> IdPath)
{
    public string Id => FileNode.IdOf(Node, Path);
}

/// <summary>
/// Answers "does this folder hold a marker file?". An interface so tests don't depend on the file system; small files
/// like <c>package.json</c> aren't individual nodes in the tree. Markers may be globs such as <c>*.csproj</c>.
/// </summary>
public interface IMarkerChecker
{
    bool Contains(string folder, string marker);

    /// <summary>The full path of the first item in <paramref name="folder"/> matching <paramref name="marker"/>, or <c>null</c>.</summary>
    string? FirstMatch(string folder, string marker);
}

/// <summary>
/// Finds artifacts in a scan result. Stops descending into a folder once it matches, so nested <c>node_modules</c> and
/// everything inside a matched cache belong to the outermost match. The exception is a folder labelled Keep: the search
/// continues inside it, because Store app folders hold WSL distributions with their own label.
/// </summary>
public sealed class RuleEngine
{
    private readonly Dictionary<string, CleanupRule> byPath;
    private readonly Dictionary<string, CleanupRule> atDriveRoot;
    private readonly List<(string Parent, string Prefix, string Containing, CleanupRule Rule)> childRules;
    private readonly List<(string Name, string Marker, CleanupRule Rule)> projectRules;
    private readonly IMarkerChecker markers;

    public RuleEngine(IReadOnlyList<CleanupRule> rules, KnownLocations locations, IMarkerChecker markers)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(locations);
        this.markers = markers ?? throw new ArgumentNullException(nameof(markers));
        byPath = new Dictionary<string, CleanupRule>(NodePath.Comparer);
        atDriveRoot = new Dictionary<string, CleanupRule>(NodePath.Comparer);
        childRules = [];
        projectRules = [];
        foreach (var rule in rules)
        {
            var matcher = rule.Matcher;
            switch (matcher.Kind)
            {
                case MatcherKind.Path:
                    byPath.TryAdd(locations.Expand(matcher.Pattern), rule);
                    break;
                case MatcherKind.AtDriveRoot:
                    atDriveRoot.TryAdd(matcher.Name, rule);
                    break;
                case MatcherKind.ChildOf:
                    childRules.Add((locations.Expand(matcher.Parent), matcher.Prefix, matcher.Containing, rule));
                    break;
                case MatcherKind.ProjectFolder:
                    projectRules.Add((matcher.Name, matcher.Marker, rule));
                    break;
            }
        }
    }

    public IReadOnlyList<CleanupMatch> Matches(FileNode root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        var found = new List<CleanupMatch>();
        var ids = new List<string>();
        Visit(NodeRef.Root(root), parentPath: null, ids, found, cancellationToken);
        return found;
    }

    private void Visit(NodeRef item, string? parentPath, List<string> ids, List<CleanupMatch> found, CancellationToken cancellationToken)
    {
        var node = item.Node;
        if (node.Kind is not (NodeKind.Directory or NodeKind.Inaccessible)) return;
        cancellationToken.ThrowIfCancellationRequested();
        string path = NodePath.Trim(item.Path);
        ids.Add(item.Id);
        try
        {
            var rule = RuleFor(path, parentPath, NodePath.Name(path), isFolder: true);
            if (rule is not null)
            {
                found.Add(new CleanupMatch(rule, node, path, ids.ToArray()));
                // Stop inside caches: everything below belongs to the same match. Folders to keep are different.
                if (rule.Safety != SafetyLevel.Keep) return;
            }
            foreach (var child in item.Children)
            {
                if (child.Node.Kind == NodeKind.File)
                {
                    // Files can't hold a project, so only exact locations apply (hiberfil.sys, pagefile.sys).
                    var fileRule = FileRuleFor(NodePath.Trim(child.Path), path, child.Node.Name);
                    if (fileRule is not null) found.Add(new CleanupMatch(fileRule, child.Node, NodePath.Trim(child.Path), [.. ids, child.Id]));
                    continue;
                }
                Visit(child, path, ids, found, cancellationToken);
            }
        }
        finally
        {
            ids.RemoveAt(ids.Count - 1);
        }
    }

    private CleanupRule? RuleFor(string path, string? parentPath, string name, bool isFolder)
    {
        if (byPath.TryGetValue(path, out var exact)) return exact;
        if (parentPath is not null && NodePath.IsDriveRoot(parentPath) && atDriveRoot.TryGetValue(name, out var rootRule)) return rootRule;
        if (parentPath is null || !isFolder) return null;
        foreach (var (parent, prefix, containing, rule) in childRules)
        {
            if (NodePath.Comparer.Equals(parent, parentPath)
                && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && name.Contains(containing, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }
        foreach (var (projectName, marker, rule) in projectRules)
        {
            if (string.Equals(projectName, name, StringComparison.OrdinalIgnoreCase) && markers.Contains(parentPath, marker)) return rule;
        }
        return null;
    }

    private CleanupRule? FileRuleFor(string path, string parentPath, string name) => RuleFor(path, parentPath, name, isFolder: false);
}
