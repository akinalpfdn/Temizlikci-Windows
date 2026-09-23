using Temizlikci.Domain.Tree;

namespace Temizlikci.Tests.Support;

/// <summary>Builds small in-memory trees for domain and view-model tests.</summary>
internal static class TreeBuilder
{
    public const string RootPath = @"C:\Scan";

    public static FileNode File(string name, long size, DateTime? modified = null) => FileNode.File(name, size, modified);

    public static FileNode Dir(string name, params FileNode[] children) => FileNode.Directory(name, null, children);

    public static FileNode Root(params FileNode[] children) => FileNode.Directory(RootPath, null, children);

    /// <summary>
    /// C:\Scan
    /// ├── Apps (600): Big.app (500), Small.app (100)
    /// ├── Docs (300): Reports (200): q1.pdf (200); notes.txt (100)
    /// └── movie.mov (100)
    /// </summary>
    public static FileNode Sample() => Root(
        Dir("Apps", File("Big.app", 500), File("Small.app", 100)),
        Dir("Docs", Dir("Reports", File("q1.pdf", 200)), File("notes.txt", 100)),
        File("movie.mov", 100));

    public static string Id(string relative) => relative.Length == 0 ? RootPath : NodePath.Join(RootPath, relative);

    /// <summary>A tree with a folder for every absolute path given, e.g. for checking that rules match their targets.</summary>
    public static FileNode Containing(string rootPath, IEnumerable<string> paths)
    {
        var components = paths
            .Select(path => NodePath.Trim(path))
            .Where(path => NodePath.IsWithin(path, rootPath))
            .Select(path => path[NodePath.Trim(rootPath).TrimEnd('\\').Length..].TrimStart('\\').Split('\\'))
            .ToList();
        return FileNode.Directory(rootPath, null, Build(components));

        static IEnumerable<FileNode> Build(List<string[]> parts)
        {
            foreach (var group in parts.GroupBy(part => part[0], StringComparer.OrdinalIgnoreCase))
            {
                var rest = group.Where(part => part.Length > 1).Select(part => part[1..]).ToList();
                yield return FileNode.Directory(group.Key, null, Build(rest));
            }
        }
    }
}
