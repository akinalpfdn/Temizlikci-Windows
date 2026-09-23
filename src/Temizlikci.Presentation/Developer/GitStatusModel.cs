using CommunityToolkit.Mvvm.ComponentModel;
using Temizlikci.Domain.Projects;

namespace Temizlikci.Presentation.Developer;

/// <summary>
/// Git state for the projects on screen, read in the background a few at a time and kept for the session. Reading is
/// lazy: only projects someone is looking at are asked about.
/// </summary>
public sealed partial class GitStatusModel(IGitInspector inspector) : ObservableObject
{
    public const int Concurrency = 3;

    private readonly IGitInspector inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    private readonly Dictionary<string, GitState> states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loading = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Projects Git couldn't read (not a repository any more, or Git not installed).</summary>
    private readonly HashSet<string> unreadable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> queue = new();
    private int running;

    /// <summary>Queues a Git project for reading, unless it was read, is being read, or couldn't be.</summary>
    public void Load(DeveloperProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Evidence != ProjectEvidence.Git) return;
        string key = project.Path;
        if (states.ContainsKey(key) || loading.Contains(key) || unreadable.Contains(key)) return;
        loading.Add(key);
        queue.Enqueue(key);
        Pump();
    }

    public GitState? State(DeveloperProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return states.GetValueOrDefault(project.Path);
    }

    public bool IsLoading(DeveloperProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return loading.Contains(project.Path);
    }

    /// <summary>Forgets what was read, so the next look asks Git again (after a rescan, for example).</summary>
    public void Reset()
    {
        states.Clear();
        unreadable.Clear();
        OnPropertyChanged(string.Empty);
    }

    private void Pump()
    {
        while (running < Concurrency && queue.Count > 0)
        {
            string path = queue.Dequeue();
            running++;
            _ = ReadAsync(path);
        }
    }

    private async Task ReadAsync(string path)
    {
        var state = await inspector.StateAsync(path, CancellationToken.None).ConfigureAwait(true);
        loading.Remove(path);
        if (state is not null) states[path] = state;
        else unreadable.Add(path);
        running--;
        OnPropertyChanged(string.Empty);
        Pump();
    }
}
