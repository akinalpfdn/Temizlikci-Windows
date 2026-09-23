using CommunityToolkit.Mvvm.ComponentModel;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Tools;
using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.Presentation.Developer;

/// <summary>
/// WSL distributions and Windows' own cleanup tools, for the Developer view: one action at a time, with what's
/// running, what it achieved, and the tool's own error when it fails. The counterpart of the Mac's simulators model.
/// </summary>
public sealed partial class WindowsToolsModel : ObservableObject
{
    private readonly IWslManager wsl;
    private readonly IComponentStore components;
    private readonly IVolumeInfoProvider volumes;
    private readonly string systemDrive;

    [ObservableProperty]
    private IReadOnlyList<WslDistribution> distributions = [];

    [ObservableProperty]
    private bool hasLoaded;

    [ObservableProperty]
    private bool isWslAvailable = true;

    /// <summary>What is running now, for the progress line; <c>null</c> when idle.</summary>
    [ObservableProperty]
    private string? working;

    /// <summary>What the last action achieved, in a sentence.</summary>
    [ObservableProperty]
    private string? lastResult;

    [ObservableProperty]
    private ActionError? error;

    /// <summary>Set once a tool changed the disk, so scans can say their sizes are outdated.</summary>
    [ObservableProperty]
    private bool didChangeDisk;

    public WindowsToolsModel(IWslManager wsl, IComponentStore components, IVolumeInfoProvider volumes, string systemDrive)
    {
        this.wsl = wsl ?? throw new ArgumentNullException(nameof(wsl));
        this.components = components ?? throw new ArgumentNullException(nameof(components));
        this.volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));
        this.systemDrive = systemDrive ?? throw new ArgumentNullException(nameof(systemDrive));
    }

    public bool IsWorking => Working is not null;

    public async Task LoadAsync()
    {
        if (IsWorking) return;
        Working = L10n.ToolsWorkingLoad;
        try
        {
            Distributions = await wsl.ListAsync(CancellationToken.None).ConfigureAwait(true);
            IsWslAvailable = true;
        }
        catch (ToolException exception) when (exception.Failure == ToolFailure.NotInstalled)
        {
            // No WSL on this PC is a state to show, not an error to report.
            Distributions = [];
            IsWslAvailable = false;
        }
        finally
        {
            Working = null;
            HasLoaded = true;
        }
    }

    public async Task CompactAsync(WslDistribution distribution)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        long? before = distribution.DiskSize;
        bool done = await Perform(L10n.ToolsWorkingCompact(distribution.Name), () => wsl.CompactAsync(distribution, CancellationToken.None)).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
        if (!done) return;
        long? after = Distributions.FirstOrDefault(item => item.Name == distribution.Name)?.DiskSize;
        LastResult = before is { } old && after is { } now && old > now
            ? L10n.WslCompacted(distribution.Name, Format.Bytes(now), Format.Bytes(old - now))
            : L10n.WslCompactedNothing(distribution.Name);
    }

    public async Task UnregisterAsync(WslDistribution distribution)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        bool done = await Perform(L10n.ToolsWorkingRemove(distribution.Name), () => wsl.UnregisterAsync(distribution, CancellationToken.None)).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
        if (done) LastResult = L10n.WslRemoved(distribution.Name);
    }

    /// <summary>Runs DISM, and reports the free space it gained on the system drive.</summary>
    public async Task CleanUpComponentsAsync()
    {
        long? before = FreeSpace();
        bool done = await Perform(L10n.ToolsWorkingComponents, () => components.CleanUpAsync(CancellationToken.None)).ConfigureAwait(true);
        if (!done) return;
        long? after = FreeSpace();
        LastResult = before is { } old && after is { } now && now > old ? L10n.ComponentsDone(Format.Bytes(now - old)) : L10n.ComponentsDoneNothing;
    }

    public void DismissError() => Error = null;

    public void DismissResult() => LastResult = null;

    partial void OnWorkingChanged(string? value) => OnPropertyChanged(nameof(IsWorking));

    private async Task<bool> Perform(string description, Func<Task> action)
    {
        if (IsWorking) return false;
        LastResult = null;
        Working = description;
        try
        {
            await action().ConfigureAwait(true);
            DidChangeDisk = true;
            return true;
        }
        catch (ToolException exception)
        {
            var (message, suggestion) = ErrorText.For(exception);
            Error = new ActionError(message, suggestion) { OffersRestart = exception.Failure == ToolFailure.NeedsAdministrator };
            return false;
        }
        finally
        {
            Working = null;
        }
    }

    private long? FreeSpace()
    {
        try
        {
            return volumes.Usage(systemDrive).AvailableCapacity;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
