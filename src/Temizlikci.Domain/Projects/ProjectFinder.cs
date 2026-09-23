using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.Projects;

/// <summary>What identified a folder as a project. Shown to explain why it is listed.</summary>
public enum ProjectEvidence
{
    Git,
    VisualStudio,
    DotNet,
    Node,
    Flutter,
    Rust,
    Gradle,
    Maven,
    Go,
    Unity,
}

/// <summary>How long a project must sit untouched before it is listed.</summary>
public enum StalePeriod
{
    Month = 30,
    Quarter = 90,
    HalfYear = 180,
    Year = 365,
}

/// <summary>A project folder found in a scan, with the build artifacts inside it.</summary>
/// <param name="IdPath">IDs from the scan's root down to the project folder.</param>
/// <param name="LastTouchedUtc">Newest change to the project's own files; <c>null</c> when nothing could be read.</param>
public sealed record DeveloperProject(
    FileNode Node,
    string Path,
    IReadOnlyList<string> IdPath,
    ProjectEvidence Evidence,
    IReadOnlyList<CleanupMatch> Artifacts,
    DateTime? LastTouchedUtc)
{
    public string Id => Path;

    public string Name => Node.Name;

    /// <summary>Bytes held by artifacts that can be moved to the Recycle Bin.</summary>
    public long ReclaimableSize => Artifacts.Where(artifact => artifact.Rule.Safety == SafetyLevel.Safe).Sum(artifact => artifact.Node.AllocatedSize);

    public bool IsStale(DateTime nowUtc, StalePeriod period) =>
        LastTouchedUtc is { } touched && nowUtc - touched >= TimeSpan.FromDays((int)period);
}

/// <summary>Reads when a project was last worked on. An interface so tests don't touch the file system.</summary>
public interface IProjectActivityReader
{
    DateTime? LastTouchedUtc(string projectPath, IReadOnlySet<string> ignoringNames);
}

/// <summary>
/// Finds project folders in a scan tree. <c>.git</c> and <c>.vs</c> (both folders) are recognized from the tree alone;
/// marker files are small, so they are only looked for next to a folder that already holds a build artifact — asking
/// the disk about every folder of a drive would mean hundreds of thousands of extra lookups. The outermost project
/// wins: a package inside <c>node_modules</c> belongs to the project that owns it.
/// </summary>
public sealed class ProjectFinder
{
    /// <summary>Marker file → what it identifies, checked next to a candidate folder, most specific first.</summary>
    public static IReadOnlyList<(string Marker, ProjectEvidence Evidence)> Markers { get; } =
    [
        ("*.sln", ProjectEvidence.VisualStudio),
        ("*.slnx", ProjectEvidence.VisualStudio),
        ("*.csproj", ProjectEvidence.DotNet),
        ("package.json", ProjectEvidence.Node),
        ("pubspec.yaml", ProjectEvidence.Flutter),
        ("Cargo.toml", ProjectEvidence.Rust),
        ("build.gradle", ProjectEvidence.Gradle),
        ("build.gradle.kts", ProjectEvidence.Gradle),
        ("pom.xml", ProjectEvidence.Maven),
        ("go.mod", ProjectEvidence.Go),
    ];

    private readonly IMarkerChecker markers;
    private readonly IProjectActivityReader activity;
    private readonly string[] ignored;

    public ProjectFinder(IMarkerChecker markers, IProjectActivityReader activity, KnownLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        this.markers = markers ?? throw new ArgumentNullException(nameof(markers));
        this.activity = activity ?? throw new ArgumentNullException(nameof(activity));
        // Tool caches and Windows itself are full of repositories nobody works on; listing them as stale projects
        // would bury the developer's own work.
        ignored =
        [
            locations.Windows, locations.ProgramFiles, locations.ProgramFilesX86, locations.ProgramData,
            NodePath.Join(locations.Home, "AppData"),
            NodePath.Join(locations.Home, ".vscode"), NodePath.Join(locations.Home, ".cargo"),
            NodePath.Join(locations.Home, ".rustup"), NodePath.Join(locations.Home, "go"),
            NodePath.Join(locations.Home, ".gradle"), NodePath.Join(locations.Home, ".m2"),
            NodePath.Join(locations.Home, ".nuget"), NodePath.Join(locations.Home, "scoop"),
            NodePath.Join(locations.SystemDrive, "$Recycle.Bin"),
        ];
    }

    /// <summary>Projects in the tree, largest reclaimable artifacts first. <paramref name="matches"/> come from the rule engine.</summary>
    public IReadOnlyList<DeveloperProject> Projects(FileNode root, IReadOnlyList<CleanupMatch> matches, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(matches);
        var artifactNames = new HashSet<string>(matches.Select(match => match.Node.Name), NodePath.Comparer);
        var candidates = new HashSet<string>(matches.Select(match => NodePath.Parent(match.Path) ?? match.Path), NodePath.Comparer);
        var artifactIds = new HashSet<string>(matches.Select(match => match.Id), StringComparer.Ordinal);
        var found = new List<DeveloperProject>();
        var ids = new List<string>();
        Visit(NodeRef.Root(root));
        return found.OrderByDescending(project => project.ReclaimableSize).ToList();

        void Visit(NodeRef folder)
        {
            if (folder.Node.Kind != NodeKind.Directory || artifactIds.Contains(folder.Id)) return;
            cancellationToken.ThrowIfCancellationRequested();
            string path = NodePath.Trim(folder.Path);
            if (ignored.Any(prefix => NodePath.IsSameOrWithin(path, prefix))) return;
            ids.Add(folder.Id);
            try
            {
                if (EvidenceFor(folder, path, candidates) is { } evidence)
                {
                    var artifacts = matches.Where(match => NodePath.IsWithin(match.Path, path)).ToList();
                    found.Add(new DeveloperProject(folder.Node, path, ids.ToArray(), evidence, artifacts,
                        activity.LastTouchedUtc(path, artifactNames)));
                    return;
                }
                foreach (var child in folder.Children) Visit(child);
            }
            finally
            {
                ids.RemoveAt(ids.Count - 1);
            }
        }
    }

    private ProjectEvidence? EvidenceFor(NodeRef folder, string path, HashSet<string> candidates)
    {
        bool hasUnitySettings = false;
        bool hasVisualStudioCache = false;
        foreach (var child in folder.Node.Children)
        {
            if (child.Kind != NodeKind.Directory) continue;
            if (string.Equals(child.Name, ".git", StringComparison.OrdinalIgnoreCase)) return ProjectEvidence.Git;
            if (string.Equals(child.Name, ".vs", StringComparison.OrdinalIgnoreCase)) hasVisualStudioCache = true;
            if (string.Equals(child.Name, "ProjectSettings", StringComparison.Ordinal)) hasUnitySettings = true;
        }
        if (hasVisualStudioCache) return ProjectEvidence.VisualStudio;
        if (!candidates.Contains(path)) return null;
        if (hasUnitySettings) return ProjectEvidence.Unity;
        foreach (var (marker, evidence) in Markers)
        {
            if (markers.Contains(path, marker)) return evidence;
        }
        return null;
    }
}
