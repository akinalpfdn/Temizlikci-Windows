using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Tools;
using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Developer;
using Temizlikci.Tests.Domain;

namespace Temizlikci.Tests.Support;

/// <summary>Answers tool commands from a script and records them; never starts a process.</summary>
internal sealed class StubToolRunner : IToolRunner
{
    private readonly Func<ToolCommand, ToolOutput> answer;

    public StubToolRunner(Func<ToolCommand, ToolOutput>? answer = null)
    {
        this.answer = answer ?? (_ => new ToolOutput(0, string.Empty, string.Empty));
    }

    public List<ToolCommand> Commands { get; } = [];

    public Task<ToolOutput> RunAsync(ToolCommand command, CancellationToken cancellationToken)
    {
        Commands.Add(command);
        return Task.FromResult(answer(command));
    }
}

/// <summary>A WSL that exists only in memory: never touches real distributions.</summary>
internal sealed class StubWsl : IWslManager
{
    public List<WslDistribution> Installed { get; } = [];
    public ToolException? Failure { get; set; }
    public ToolException? ListFailure { get; set; }
    /// <summary>What compacting leaves the disk at.</summary>
    public long? CompactedSize { get; set; }
    public List<string> Compacted { get; } = [];
    public List<string> Removed { get; } = [];

    public Task<IReadOnlyList<WslDistribution>> ListAsync(CancellationToken cancellationToken) =>
        ListFailure is not null ? Task.FromException<IReadOnlyList<WslDistribution>>(ListFailure) : Task.FromResult<IReadOnlyList<WslDistribution>>(Installed.ToList());

    public Task CompactAsync(WslDistribution distribution, CancellationToken cancellationToken)
    {
        if (Failure is not null) return Task.FromException(Failure);
        Compacted.Add(distribution.Name);
        int index = Installed.FindIndex(item => item.Name == distribution.Name);
        if (index >= 0 && CompactedSize is { } size) Installed[index] = Installed[index] with { DiskSize = size };
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(WslDistribution distribution, CancellationToken cancellationToken)
    {
        if (Failure is not null) return Task.FromException(Failure);
        Removed.Add(distribution.Name);
        Installed.RemoveAll(item => item.Name == distribution.Name);
        return Task.CompletedTask;
    }

    public static WslDistribution Distribution(string name, long size, int version = 2, bool running = false) =>
        new(new WslRegistration(name, $@"\\?\C:\WSL\{name}", version, "ext4.vhdx", IsDefault: false), running, size);
}

internal sealed class StubComponentStore : IComponentStore
{
    public int Runs { get; private set; }
    public ToolException? Failure { get; set; }
    /// <summary>Runs as DISM "finishes", e.g. to change the free space the model reads afterwards.</summary>
    public Action? OnRun { get; set; }

    public Task CleanUpAsync(CancellationToken cancellationToken)
    {
        if (Failure is not null) return Task.FromException(Failure);
        Runs++;
        OnRun?.Invoke();
        return Task.CompletedTask;
    }
}

internal sealed class StubEditors(params Editor[] installed) : IEditorLauncher
{
    public List<Editor> Started { get; } = [];
    public List<EditorTarget> Opened { get; } = [];

    public bool IsInstalled(Editor editor) => installed.Contains(editor);

    public void Open(EditorTarget target) => Opened.Add(target);

    public void Start(Editor editor) => Started.Add(editor);
}

/// <summary>Free space that can change between reads, as a tool's work would change it.</summary>
internal sealed class MutableVolume : IVolumeInfoProvider
{
    public long Free { get; set; } = 100L << 30;

    public IReadOnlyList<VolumeDescription> FixedVolumes() => [new(@"C:\", "Windows", "NTFS", true, true)];

    public VolumeDescription? VolumeContaining(string path) => FixedVolumes()[0];

    public VolumeUsage Usage(string path) => new(500L << 30, Free);
}

/// <summary>Everything the insight views hand people over to, as stubs.</summary>
internal sealed class ToolsFixture
{
    public StubWsl Wsl { get; } = new();
    public StubComponentStore Components { get; } = new();
    public MutableVolume Volume { get; } = new();
    public StubEditors Editors { get; init; } = new(Editor.AndroidStudio);
    public RecordingShell Shell { get; } = new();
    public StubGit Git { get; } = new();
    public StubMarkers Markers { get; init; } = new();

    public InsightTools Make() => new(new WindowsToolsModel(Wsl, Components, Volume, @"C:\"), new GitStatusModel(Git), Editors, Markers, Shell);
}

/// <summary>Git answers from a table; counts how many reads run at once.</summary>
internal sealed class StubGit : IGitInspector
{
    private readonly Dictionary<string, TaskCompletionSource<GitState?>> pending = new(StringComparer.OrdinalIgnoreCase);

    public int Running { get; private set; }
    public int MostAtOnce { get; private set; }
    public List<string> Asked { get; } = [];

    public Task<GitState?> StateAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        Asked.Add(repositoryPath);
        Running++;
        MostAtOnce = Math.Max(MostAtOnce, Running);
        var source = new TaskCompletionSource<GitState?>();
        pending[repositoryPath] = source;
        return source.Task;
    }

    /// <summary>Finishes one read; <c>null</c> means Git couldn't read the repository.</summary>
    public void Answer(string repositoryPath, GitState? state)
    {
        Running--;
        pending[repositoryPath].SetResult(state);
    }
}
