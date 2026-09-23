using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.Scanning;

public sealed record ScanConfiguration
{
    /// <summary>Files at least this large become their own node; smaller ones are summed per directory.</summary>
    public long IndividualFileThreshold { get; init; } = 10L * 1024 * 1024;

    /// <summary>Directory levels scanned in parallel tasks; deeper levels are walked within those tasks.</summary>
    public int ParallelDepth { get; init; } = 2;

    /// <summary>At most this many directories are read at the same time.</summary>
    public int MaxConcurrency { get; init; } = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);

    /// <summary>How often progress snapshots are published while scanning.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Paths below the root that are never visited nor shown.</summary>
    public IReadOnlySet<string> SkippedPaths { get; init; } = new HashSet<string>(NodePath.Comparer);

    /// <summary>Prefer the NTFS Master File Table when the process may read the volume directly.</summary>
    public bool AllowMasterFileTable { get; init; } = true;

    public static ScanConfiguration Standard { get; } = new();
}

/// <summary>How a scan read the disk, for the footer and for measurements.</summary>
public enum ScanMethod
{
    DirectoryWalk,
    MasterFileTable,
}

/// <summary>A snapshot of a running scan.</summary>
public sealed record ScanProgress
{
    public long AllocatedSize { get; init; }
    public long FileCount { get; init; }
    public long DirectoryCount { get; init; }
    public long InaccessibleCount { get; init; }
    public string? CurrentDirectory { get; init; }

    /// <summary>0…1 when the scanner knows how far it is (the MFT reader does); <c>null</c> otherwise.</summary>
    public double? Fraction { get; init; }

    /// <summary>Top-level children of the scan root that are fully measured, for progressive rendering.</summary>
    public IReadOnlyList<FileNode> CompletedTopLevel { get; init; } = [];

    /// <summary>Space measured so far inside top-level children still being scanned, by name, so the chart can grow
    /// while a large folder like Users is read instead of waiting for it to finish.</summary>
    public IReadOnlyDictionary<string, long> MeasuringTopLevel { get; init; } = new Dictionary<string, long>();
}

public sealed record ScanResult(
    FileNode Root,
    TimeSpan Duration,
    long FileCount,
    long DirectoryCount,
    long InaccessibleCount,
    ScanMethod Method);

public abstract record ScanEvent
{
    private ScanEvent()
    {
    }

    public sealed record Progress(ScanProgress Snapshot) : ScanEvent;

    public sealed record Finished(ScanResult Result) : ScanEvent;
}

public enum ScanFailure
{
    RootNotFound,
    RootNotFolder,
    RootUnreadable,
}

/// <summary>An unusable scan root. The message shown to people comes from the presentation layer.</summary>
public sealed class ScanException : Exception
{
    public ScanException(ScanFailure failure, string path)
        : base($"{failure}: {path}")
    {
        Failure = failure;
        Path = path;
    }

    public ScanException()
    {
        Path = string.Empty;
    }

    public ScanException(string message)
        : base(message)
    {
        Path = string.Empty;
    }

    public ScanException(string message, Exception innerException)
        : base(message, innerException)
    {
        Path = string.Empty;
    }

    public ScanFailure Failure { get; }

    public string Path { get; }
}

/// <summary>Measures a folder and everything below it.</summary>
public interface IDiskScanner
{
    /// <summary>
    /// Streams progress snapshots and ends with <see cref="ScanEvent.Finished"/>, or throws <see cref="ScanException"/>
    /// for an unusable root. Cancelling the token stops the scan promptly.
    /// </summary>
    IAsyncEnumerable<ScanEvent> ScanAsync(string root, ScanConfiguration configuration, CancellationToken cancellationToken);
}
