using System.ComponentModel;
using System.Globalization;
using System.Text;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Projects;

namespace Temizlikci.Services.Projects;

/// <summary>Reads a repository's local-only work with Git for Windows, never changing the repository.</summary>
public sealed class GitInspector : IGitInspector
{
    /// <summary>Branches without an upstream are counted one by one; beyond this many, the rest are skipped.</summary>
    private const int BranchLimit = 12;

    private readonly IToolRunner runner;
    private readonly Lazy<string?> git;

    public GitInspector(IToolRunner runner)
        : this(runner, Locate)
    {
    }

    /// <summary>For tests: where Git lives comes from the caller.</summary>
    public GitInspector(IToolRunner runner, Func<string?> locateGit)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        ArgumentNullException.ThrowIfNull(locateGit);
        git = new Lazy<string?>(locateGit, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<GitState?> StateAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositoryPath);
        if (git.Value is not { } executable) return null;
        if (await Git(executable, repositoryPath, ["status", "--porcelain=v2", "--branch", "--show-stash"], cancellationToken).ConfigureAwait(false) is not { } status)
        {
            return null;
        }
        var state = new GitState();
        state.ApplyStatus(status);

        string remotes = await Git(executable, repositoryPath, ["remote"], cancellationToken).ConfigureAwait(false) ?? string.Empty;
        state.HasRemote = remotes.Trim().Length > 0;
        state.UnpushedCommits = Count(await Git(executable, repositoryPath, ["rev-list", "--count", "--branches", "--not", "--remotes"], cancellationToken).ConfigureAwait(false));

        if (state.HasRemote && state.UnpushedCommits > 0
            && await Git(executable, repositoryPath, ["for-each-ref", "--format=%(refname:short)%09%(upstream:short)%09%(upstream:track)", "refs/heads"], cancellationToken).ConfigureAwait(false) is { } refs)
        {
            var (ahead, withoutUpstream) = GitState.BranchesToCheck(refs);
            var branches = ahead.ToList();
            foreach (var name in withoutUpstream.Take(BranchLimit))
            {
                int commits = Count(await Git(executable, repositoryPath, ["rev-list", "--count", name, "--not", "--remotes"], cancellationToken).ConfigureAwait(false));
                if (commits > 0) branches.Add(new GitState.UnpushedBranch(name, commits));
            }
            state.UnpushedBranches = branches.OrderByDescending(branch => branch.Commits).ToList();
        }
        return state;
    }

    private async Task<string?> Git(string executable, string repository, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        // --no-optional-locks keeps `status` from rewriting .git/index, which would make a project untouched for months
        // look as if it was worked on today. core.fsmonitor=false: a repository's own config must not get to run a
        // program just because this app looked at it.
        string[] command = ["--no-optional-locks", "-c", "core.fsmonitor=false", "-C", repository, .. arguments];
        try
        {
            var output = await runner.RunAsync(new ToolCommand(executable, command) { OutputEncoding = Encoding.UTF8 }, cancellationToken).ConfigureAwait(false);
            return output.ExitCode == 0 ? output.StandardOutput : null;
        }
        catch (Win32Exception)
        {
            // Git vanished since it was found (uninstalled while the app runs): the repository reads as unreadable.
            return null;
        }
    }

    private static int Count(string? output) =>
        int.TryParse(output?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    /// <summary>Git for Windows on the PATH, else its usual install folders (machine-wide, then per user).</summary>
    private static string? Locate()
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Join(folder.Trim('"'), "git.exe");
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry names no folder; the next one may.
                continue;
            }
            if (File.Exists(candidate)) return candidate;
        }
        string[] installs =
        [
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"),
        ];
        return installs.FirstOrDefault(File.Exists);
    }
}
