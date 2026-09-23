using System.Globalization;
using System.Text.RegularExpressions;

namespace Temizlikci.Domain.Projects;

/// <summary>Work in a Git repository that exists only on this PC: what would be lost if the folder went away.</summary>
public sealed partial class GitState
{
    public sealed record UnpushedBranch(string Name, int Commits);

    /// <summary>The checked-out branch, or <c>null</c> when HEAD is detached.</summary>
    public string? Branch { get; set; }
    public string? Upstream { get; set; }
    /// <summary>Commits on the current branch that its upstream doesn't have.</summary>
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public int Staged { get; set; }
    public int Unstaged { get; set; }
    public int Untracked { get; set; }
    public int Conflicted { get; set; }
    public int Stashes { get; set; }
    /// <summary>Commits on any local branch that no remote-tracking branch contains.</summary>
    public int UnpushedCommits { get; set; }
    public bool HasRemote { get; set; } = true;
    /// <summary>Local branches with commits that exist nowhere else.</summary>
    public IReadOnlyList<UnpushedBranch> UnpushedBranches { get; set; } = [];

    public bool HasUncommittedChanges => Staged + Unstaged + Untracked + Conflicted > 0;

    /// <summary>True when deleting the folder would lose something: uncommitted files, stashes, or unpushed commits.</summary>
    public bool HasLocalOnlyWork => HasUncommittedChanges || Stashes > 0 || UnpushedCommits > 0 || !HasRemote;

    [GeneratedRegex(@"ahead (\d+)")]
    private static partial Regex AheadCount();

    /// <summary>Reads <c>git status --porcelain=v2 --branch --show-stash</c>.</summary>
    public void ApplyStatus(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                string head = line["# branch.head ".Length..];
                Branch = head == "(detached)" ? null : head;
            }
            else if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                Upstream = line["# branch.upstream ".Length..];
            }
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                var parts = line["# branch.ab ".Length..].Split(' ');
                if (parts.Length == 2)
                {
                    Ahead = ParseInt(parts[0].TrimStart('+'));
                    Behind = ParseInt(parts[1].TrimStart('-'));
                }
            }
            else if (line.StartsWith("# stash ", StringComparison.Ordinal))
            {
                Stashes = ParseInt(line["# stash ".Length..]);
            }
            else if (line.StartsWith("1 ", StringComparison.Ordinal) || line.StartsWith("2 ", StringComparison.Ordinal))
            {
                // "1 XY ...": X is the index (staged) side, Y the work tree (unstaged) side; "." = unchanged.
                var fields = line.Split(' ', 3);
                if (fields.Length < 2 || fields[1].Length != 2) continue;
                if (fields[1][0] != '.') Staged++;
                if (fields[1][1] != '.') Unstaged++;
            }
            else if (line.StartsWith("u ", StringComparison.Ordinal))
            {
                Conflicted++;
            }
            else if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                Untracked++;
            }
        }
    }

    /// <summary>
    /// Reads <c>git for-each-ref --format=%(refname:short)%09%(upstream:short)%09%(upstream:track) refs/heads</c> and
    /// returns the branches whose commits may exist only here: ahead of their upstream, or with no upstream at all.
    /// </summary>
    public static (IReadOnlyList<UnpushedBranch> Ahead, IReadOnlyList<string> WithoutUpstream) BranchesToCheck(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var ahead = new List<UnpushedBranch>();
        var withoutUpstream = new List<string>();
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = raw.TrimEnd('\r').Split('\t');
            string name = fields[0];
            if (name.Length == 0) continue;
            string upstream = fields.Length > 1 ? fields[1] : string.Empty;
            string track = fields.Length > 2 ? fields[2] : string.Empty;
            if (upstream.Length == 0)
            {
                withoutUpstream.Add(name);
            }
            else if (AheadCount().Match(track) is { Success: true } match)
            {
                int commits = ParseInt(match.Groups[1].Value);
                if (commits > 0) ahead.Add(new UnpushedBranch(name, commits));
            }
        }
        return (ahead, withoutUpstream);
    }

    private static int ParseInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}

/// <summary>Reads a repository's local-only work. <c>null</c> when Git isn't available or the folder isn't a repository.</summary>
public interface IGitInspector
{
    Task<GitState?> StateAsync(string repositoryPath, CancellationToken cancellationToken);
}
