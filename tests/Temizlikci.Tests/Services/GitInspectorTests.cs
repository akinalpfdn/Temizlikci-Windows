using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Tree;
using Temizlikci.Presentation.Developer;
using Temizlikci.Services.Projects;
using Temizlikci.Services.Tools;
using Temizlikci.Tests.Support;

namespace Temizlikci.Tests.Services;

public sealed class GitInspectorTests : IDisposable
{
    private readonly FixtureTree tree = new();

    public void Dispose() => tree.Dispose();

    /// <summary>Runs real Git in the fixture folder with a throwaway identity, never the person's signing setup.</summary>
    private static async Task Git(string folder, params string[] arguments)
    {
        string[] command = ["-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", "-C", folder, .. arguments];
        var output = await new ProcessToolRunner().RunAsync(new ToolCommand("git", command), TestContext.Current.CancellationToken);
        Assert.True(output.ExitCode == 0, output.StandardError);
    }

    private static bool GitIsInstalled() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Any(folder => File.Exists(Path.Join(folder.Trim('"'), "git.exe")));

    [Fact]
    public async Task Should_LeaveTheIndexDateAlone_When_ReadingARepository()
    {
        if (!GitIsInstalled()) Assert.Skip("Git for Windows isn't installed.");
        string repository = tree.Folder("project");
        tree.File(@"project\readme.txt", 10);
        await Git(repository, "init", "--quiet");
        await Git(repository, "add", ".");
        await Git(repository, "commit", "--quiet", "-m", "first");
        File.WriteAllText(Path.Join(repository, "readme.txt"), "changed since the commit");
        string index = Path.Join(repository, ".git", "index");
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(index, old);

        var state = await new GitInspector(new ProcessToolRunner()).StateAsync(repository, TestContext.Current.CancellationToken);

        Assert.Equal(old, File.GetLastWriteTimeUtc(index));
        Assert.NotNull(state);
        Assert.Equal(1, state.Unstaged);
        Assert.False(state.HasRemote);
        Assert.True(state.HasLocalOnlyWork);
    }

    [Fact]
    public async Task Should_ReadNothing_When_TheFolderIsNotARepository()
    {
        if (!GitIsInstalled()) Assert.Skip("Git for Windows isn't installed.");

        Assert.Null(await new GitInspector(new ProcessToolRunner()).StateAsync(tree.Folder("plain"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_NeverRunAnything_When_GitIsNotInstalled()
    {
        var runner = new StubToolRunner();

        Assert.Null(await new GitInspector(runner, () => null).StateAsync(@"C:\project", TestContext.Current.CancellationToken));
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Should_ReadWithoutLocksOrTheRepositorysMonitor_When_AskingGit()
    {
        var runner = new StubToolRunner(_ => new ToolOutput(0, "# branch.head main\n", string.Empty));

        await new GitInspector(runner, () => @"C:\Git\cmd\git.exe").StateAsync(@"C:\project", TestContext.Current.CancellationToken);

        Assert.All(runner.Commands, command => Assert.Equal(["--no-optional-locks", "-c", "core.fsmonitor=false", "-C", @"C:\project"], command.Arguments.Take(5)));
    }

    [Fact]
    public async Task Should_ListBranchesThatExistOnlyHere_When_CommitsAreUnpushed()
    {
        var runner = new StubToolRunner(command => string.Join(' ', command.Arguments.Skip(5)) switch
        {
            "remote" => new ToolOutput(0, "origin\n", string.Empty),
            "rev-list --count --branches --not --remotes" => new ToolOutput(0, "5\n", string.Empty),
            var refs when refs.StartsWith("for-each-ref", StringComparison.Ordinal) => new ToolOutput(0, "main\torigin/main\t[ahead 2]\nspike\t\t\n", string.Empty),
            "rev-list --count spike --not --remotes" => new ToolOutput(0, "3\n", string.Empty),
            _ => new ToolOutput(0, "# branch.head main\n", string.Empty),
        });

        var state = await new GitInspector(runner, () => "git.exe").StateAsync(@"C:\project", TestContext.Current.CancellationToken);

        Assert.NotNull(state);
        Assert.Equal([new GitState.UnpushedBranch("spike", 3), new GitState.UnpushedBranch("main", 2)], state.UnpushedBranches);
    }
}

public sealed class GitStatusModelTests
{
    private readonly StubGit git = new();

    private static DeveloperProject Project(string name, ProjectEvidence evidence = ProjectEvidence.Git) =>
        new(FileNode.Directory(name, null, []), $@"C:\src\{name}", [$@"C:\src\{name}"], evidence, [], null);

    [Fact]
    public void Should_ReadThreeRepositoriesAtATime_When_ManyAreShown()
    {
        var model = new GitStatusModel(git);

        foreach (var name in new[] { "a", "b", "c", "d", "e" }) model.Load(Project(name));

        Assert.Equal(3, git.MostAtOnce);
        Assert.Equal(3, git.Asked.Count);
        git.Answer(@"C:\src\a", new GitState());
        Assert.Equal(4, git.Asked.Count);
        Assert.NotNull(model.State(Project("a")));
        Assert.True(model.IsLoading(Project("e")));
    }

    [Fact]
    public void Should_AskOnlyOnce_When_GitCouldNotReadAProject()
    {
        var model = new GitStatusModel(git);
        model.Load(Project("broken"));
        git.Answer(@"C:\src\broken", null);

        model.Load(Project("broken"));

        Assert.Single(git.Asked);
        Assert.Null(model.State(Project("broken")));
        Assert.False(model.IsLoading(Project("broken")));
    }

    [Fact]
    public void Should_IgnoreProjectsWithoutGit_When_Loading()
    {
        new GitStatusModel(git).Load(Project("web", ProjectEvidence.Node));

        Assert.Empty(git.Asked);
    }
}
