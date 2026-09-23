using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Identity;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Actions;

namespace Temizlikci.Presentation.Overview;

/// <summary>A place that can be scanned, as the person sees it.</summary>
/// <param name="IsWholeVolume">True for a drive: its results include the volume's unattributed space.</param>
public sealed record ScanLocation(string Path, string DisplayName, bool IsWholeVolume);

public enum ScanPhase
{
    Idle,
    Scanning,
    Finished,
    Failed,
}

/// <summary>A failed action, shown as a dialog with what happened and what to do next.</summary>
public sealed record ActionError(string Message, string? Suggestion);

/// <summary>What a location's model works with. One bundle, shared by every location and built by the composition root.</summary>
public sealed class LocationServices
{
    public required IDiskScanner Scanner { get; init; }
    public required IVolumeInfoProvider Volumes { get; init; }
    public required IShell Shell { get; init; }
    public required IRecycleBin RecycleBin { get; init; }
    public required RecycleLedger Ledger { get; init; }
    public required UndoHistory Undo { get; init; }
    public required RuleEngine Rules { get; init; }
    public required ProjectFinder Projects { get; init; }
    public required SystemProtection Protection { get; init; }
    public required FolderIdentifier Identifier { get; init; }
    public required IScanCache Cache { get; init; }
    public required ISnapshotStore Snapshots { get; init; }
    public required IHiddenSpaceReader HiddenSpace { get; init; }
    public ScanConfiguration Configuration { get; init; } = ScanConfiguration.Standard;
    public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

    /// <summary>How many saved snapshots each location keeps.</summary>
    public int SnapshotsKept { get; init; } = 10;
}
