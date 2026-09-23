using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Projects;

namespace Temizlikci.Services.Projects;

/// <summary>Looks for marker files (<c>package.json</c>, <c>*.csproj</c>) in a folder on disk.</summary>
public sealed class FileSystemMarkerChecker : IMarkerChecker
{
    public bool Contains(string folder, string marker) => FirstMatch(folder, marker) is not null;

    public string? FirstMatch(string folder, string marker)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(marker);
        try
        {
            if (marker.Contains('*', StringComparison.Ordinal))
            {
                return Directory.EnumerateFileSystemEntries(folder, marker, new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true })
                    .Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            }
            string candidate = Path.Join(folder, marker);
            return Path.Exists(candidate) ? candidate : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder that can't be listed has no marker we could act on.
            return null;
        }
    }
}

/// <summary>
/// Reads when a project was last worked on, without walking the whole project. Folder dates only change when entries
/// are added or removed, so editing a file leaves its folder's date untouched. Git's index is written on every stage,
/// commit and checkout, which makes it the best signal; otherwise the newest file in the project's own top two levels
/// is used, skipping build output.
/// </summary>
public sealed class FileSystemProjectActivity : IProjectActivityReader
{
    /// <summary>How deep to look for recently changed files when there is no Git index.</summary>
    public const int Depth = 2;

    /// <summary>Never descended into: they change when tools run, not when the developer works.</summary>
    private static readonly HashSet<string> AlwaysIgnored = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "build", ".build", "target", ".dart_tool", ".next", "bin", "obj", ".vs", ".idea",
        "Pods", ".gradle", "vendor", "dist", "out", "__pycache__", ".venv", "venv", "packages",
    };

    public DateTime? LastTouchedUtc(string projectPath, IReadOnlySet<string> ignoringNames)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(ignoringNames);
        if (GitActivity(projectPath) is { } git) return git;
        var ignored = new HashSet<string>(AlwaysIgnored, StringComparer.OrdinalIgnoreCase);
        ignored.UnionWith(ignoringNames);
        return NewestFile(projectPath, ignored, Depth);
    }

    /// <summary>The newest of .git\index and .git\HEAD: staging, committing and switching branches rewrite one of them.</summary>
    private static DateTime? GitActivity(string project)
    {
        string git = Path.Join(project, ".git");
        DateTime? newest = null;
        foreach (var name in new[] { "index", "HEAD" })
        {
            var file = new FileInfo(Path.Join(git, name));
            if (file.Exists && (newest is null || file.LastWriteTimeUtc > newest)) newest = file.LastWriteTimeUtc;
        }
        return newest;
    }

    private static DateTime? NewestFile(string folder, HashSet<string> ignored, int depth)
    {
        if (depth <= 0) return null;
        DateTime? newest = null;
        try
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                DateTime? date = entry is DirectoryInfo directory
                    ? ignored.Contains(directory.Name) || directory.Name.StartsWith('.') ? null : NewestFile(directory.FullName, ignored, depth - 1)
                    : entry.LastWriteTimeUtc;
                if (date is { } value && (newest is null || value > newest)) newest = value;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What can't be read simply doesn't count toward the date.
        }
        return newest;
    }
}
