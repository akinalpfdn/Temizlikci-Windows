using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Tree;
using static Temizlikci.Tests.Support.TreeBuilder;

namespace Temizlikci.Tests.Domain;

internal sealed class FixedActivity(Dictionary<string, DateTime> dates) : IProjectActivityReader
{
    public DateTime? LastTouchedUtc(string projectPath, IReadOnlySet<string> ignoringNames) =>
        dates.FirstOrDefault(entry => NodePath.Comparer.Equals(entry.Key, projectPath)) is { Key: not null } found ? found.Value : null;
}

public sealed class ProjectFinderTests
{
    private const long Mb = 1_000_000;
    private static readonly DateTime Now = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);

    private static readonly StubMarkers Markers = new(new()
    {
        [@"C:\Code\Site"] = ["package.json"],
        [@"C:\Code\App"] = ["pubspec.yaml"],
    });

    /// <summary>
    /// C:\Code
    /// ├── App (has .git and build output)
    /// ├── Site (no .git; package.json next to node_modules, which holds a nested repository)
    /// └── Notes (no project at all)
    /// </summary>
    private static FileNode Tree() => FileNode.Directory(@"C:\Code", null,
    [
        Dir("App", Dir(".git", File("pack", 20 * Mb)), Dir("build", File("out", 800 * Mb))),
        Dir("Site", Dir("node_modules", File("lib", 400 * Mb), Dir("inner", Dir(".git")))),
        Dir("Notes", File("draft", 30 * Mb)),
    ]);

    private static IReadOnlyList<CleanupMatch> Matches(FileNode root) =>
        new RuleEngine(CleanupCatalog.Rules, KnownLocations.Sample, Markers).Matches(root);

    private static ProjectFinder Finder(Dictionary<string, DateTime>? dates = null) =>
        new(Markers, new FixedActivity(dates ?? []), KnownLocations.Sample);

    [Fact]
    public void Should_FindProjectsByGitOrMarkerAndKeepTheOutermost_When_WalkingATree()
    {
        var root = Tree();

        var projects = Finder().Projects(root, Matches(root));

        Assert.Equal(["App", "Site"], projects.Select(project => project.Name));
        Assert.Equal([ProjectEvidence.Git, ProjectEvidence.Node], projects.Select(project => project.Evidence));
        Assert.DoesNotContain(projects, project => project.Path.Contains("node_modules", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_GiveEachProjectTheArtifactsInsideIt_When_Grouping()
    {
        var root = Tree();

        var projects = Finder().Projects(root, Matches(root));

        var app = projects.Single(project => project.Name == "App");
        Assert.Equal(["build"], app.Artifacts.Select(artifact => artifact.Node.Name));
        Assert.Equal(800 * Mb, app.ReclaimableSize);
        Assert.Equal(["node_modules"], projects.Single(project => project.Name == "Site").Artifacts.Select(a => a.Node.Name));
    }

    [Fact]
    public void Should_CountAProjectAsStaleOnlyAfterThePeriod_When_ItHasADate()
    {
        var root = Tree();
        var projects = Finder(new() { [@"C:\Code\App"] = Now.AddDays(-100) }).Projects(root, Matches(root));
        var app = projects.Single(project => project.Name == "App");
        var site = projects.Single(project => project.Name == "Site");

        Assert.True(app.IsStale(Now, StalePeriod.Quarter));
        Assert.False(app.IsStale(Now, StalePeriod.HalfYear));
        Assert.False(site.IsStale(Now, StalePeriod.Month));
    }

    [Fact]
    public void Should_RecognizeVisualStudioSolutions_When_TheyHoldASolutionCache()
    {
        var root = FileNode.Directory(@"C:\Code", null, [Dir("Api", Dir(".vs", File("db", 300 * Mb)))]);

        var projects = Finder().Projects(root, []);

        Assert.Equal([ProjectEvidence.VisualStudio], projects.Select(project => project.Evidence));
    }

    [Fact]
    public void Should_IgnoreRepositoriesThatBelongToTools_When_TheyLiveInAppDataOrProgramFiles()
    {
        var root = Containing(@"C:\", [
            @"C:\Program Files\Git\.git",
            @"C:\Users\dev\AppData\Local\Temp\repo\.git",
            @"C:\Users\dev\scoop\buckets\main\.git",
            @"C:\Users\dev\Work\.git",
        ]);

        var projects = Finder().Projects(root, []);

        Assert.Equal(["Work"], projects.Select(project => project.Name));
    }
}

public sealed class GitStateTests
{
    [Fact]
    public void Should_ReadBranchAheadBehindStashesAndChanges_When_ParsingPorcelainV2()
    {
        const string output = """
            # branch.oid 1234567890abcdef
            # branch.head main
            # branch.upstream origin/main
            # branch.ab +2 -1
            # stash 3
            1 M. N... 100644 100644 100644 aaa bbb staged.cs
            1 .M N... 100644 100644 100644 aaa bbb edited.cs
            1 MM N... 100644 100644 100644 aaa bbb both.cs
            2 R. N... 100644 100644 100644 aaa bbb R100 new.cs	old.cs
            u UU N... 100644 100644 100644 100644 aaa bbb ccc conflict.cs
            ? notes.txt
            ? scratch/
            """;
        var state = new GitState();

        state.ApplyStatus(output.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal("main", state.Branch);
        Assert.Equal("origin/main", state.Upstream);
        Assert.Equal(2, state.Ahead);
        Assert.Equal(1, state.Behind);
        Assert.Equal(3, state.Stashes);
        Assert.Equal(3, state.Staged);
        Assert.Equal(2, state.Unstaged);
        Assert.Equal(1, state.Conflicted);
        Assert.Equal(2, state.Untracked);
        Assert.True(state.HasLocalOnlyWork);
    }

    [Fact]
    public void Should_HaveNothingToLose_When_TheRepositoryIsCleanAndPushed()
    {
        var clean = new GitState();
        clean.ApplyStatus("# branch.oid abc\n# branch.head main\n# branch.upstream origin/main\n# branch.ab +0 -0\n");

        Assert.False(clean.HasLocalOnlyWork);
    }

    [Fact]
    public void Should_ReportNoBranch_When_HeadIsDetached()
    {
        var detached = new GitState();
        detached.ApplyStatus("# branch.oid abc\n# branch.head (detached)\n");

        Assert.Null(detached.Branch);
    }

    [Fact]
    public void Should_FlagLocalWork_When_ThereIsNoRemote()
    {
        Assert.True(new GitState { HasRemote = false }.HasLocalOnlyWork);
    }

    [Fact]
    public void Should_ListBranchesAheadAndWithoutUpstream_When_ReadingRefs()
    {
        const string output = "main\torigin/main\t\nfeature\torigin/feature\t[ahead 4]\nmixed\torigin/mixed\t[ahead 1, behind 2]\nspike\t\t\ngone\torigin/gone\t[gone]\n";

        var (ahead, withoutUpstream) = GitState.BranchesToCheck(output);

        Assert.Equal([new GitState.UnpushedBranch("feature", 4), new GitState.UnpushedBranch("mixed", 1)], ahead);
        Assert.Equal(["spike"], withoutUpstream);
    }
}

public sealed class ProjectEditorsTests
{
    private static DeveloperProject Project() =>
        new(Dir("app"), @"C:\Work\app", [@"C:\Work\app"], ProjectEvidence.Git, [], null);

    private static StubMarkers Files(params string[] names) => new(new() { [@"C:\Work\app"] = names });

    [Fact]
    public void Should_OpenTheSolutionInVisualStudio_When_ThereIsOne()
    {
        var targets = ProjectEditors.Targets(Project(), Files("App.csproj", "App.sln"));

        Assert.Equal(new EditorTarget(Editor.VisualStudio, @"C:\Work\app\App.sln"), targets[0]);
    }

    [Fact]
    public void Should_OpenTheProjectFile_When_ThereIsNoSolution()
    {
        var targets = ProjectEditors.Targets(Project(), Files("App.csproj"));

        Assert.Equal(@"C:\Work\app\App.csproj", targets[0].Path);
    }

    [Fact]
    public void Should_OfferAndroidStudioForGradleAndFlutterAndCodeForEverything_When_ChoosingEditors()
    {
        Assert.Equal([Editor.AndroidStudio, Editor.VisualStudioCode], ProjectEditors.Targets(Project(), Files("build.gradle.kts")).Select(t => t.Editor));
        Assert.Equal([Editor.AndroidStudio, Editor.VisualStudioCode], ProjectEditors.Targets(Project(), Files("pubspec.yaml")).Select(t => t.Editor));
        Assert.Equal([Editor.VisualStudioCode], ProjectEditors.Targets(Project(), Files()).Select(t => t.Editor));
    }
}
