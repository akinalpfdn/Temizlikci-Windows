using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;
using Temizlikci.Services.Scanning.Mft;

namespace Temizlikci.Services.Scanning;

/// <summary>
/// Measures a folder by reading its volume's NTFS Master File Table instead of listing every directory: seconds for
/// millions of files. Needs administrator rights. Throws <see cref="MftUnavailableException"/> before any result when
/// the table can't be used, so <see cref="AdaptiveScanner"/> can fall back to the directory walk.
/// </summary>
public sealed class MftScanner : IDiskScanner
{
    /// <summary>A whole-volume result larger than the volume's used space by more than this is a parsing error.</summary>
    private const double UsedSpaceTolerance = 1.02;

    /// <summary>Folders on NTFS nest deeper than a thread-pool stack comfortably recurses.</summary>
    private const int BuilderStackSize = 64 * 1024 * 1024;

    public async IAsyncEnumerable<ScanEvent> ScanAsync(string root, ScanConfiguration configuration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(configuration);
        string trimmed = NodePath.Trim(root);
        var channel = Channel.CreateUnbounded<ScanEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                channel.Writer.TryWrite(new ScanEvent.Finished(Run(trimmed, configuration, channel.Writer, stop.Token)));
                channel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
            finally
            {
                done.TrySetResult();
            }
        }, BuilderStackSize)
        { IsBackground = true, Name = "Temizlikci MFT scan" };
        thread.Start();

        try
        {
            await foreach (var scanEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return scanEvent;
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await done.Task.ConfigureAwait(false);
        }
    }

    private static ScanResult Run(string root, ScanConfiguration configuration, ChannelWriter<ScanEvent> events, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        DirectoryScanner.ValidateRoot(root);
        string volumeRoot = Path.GetPathRoot(root) ?? throw new MftUnavailableException($"{root} has no volume root.");
        using var reader = MftVolumeReader.Open(volumeRoot);
        var extents = reader.Extents();
        var table = new MftRecordTable((int)Math.Min(reader.RecordCount, int.MaxValue - 1));
        long threshold = configuration.IndividualFileThreshold;
        var lastReport = Stopwatch.StartNew();

        reader.ReadAll(extents, (number, record) =>
        {
            if (MftRecordParser.TryParse(record, reader.BytesPerCluster, out var parsed)) table.Apply(number, parsed, threshold);
        }, fraction =>
        {
            if (lastReport.Elapsed < configuration.ProgressInterval) return;
            lastReport.Restart();
            events.TryWrite(new ScanEvent.Progress(new ScanProgress { Fraction = fraction, CurrentDirectory = volumeRoot }));
        }, cancellationToken);

        var missing = table.MissingNames(threshold).ToList();
        if (missing.Count > 0)
        {
            reader.ReadRecords(extents, missing, (number, record) =>
            {
                if (MftRecordParser.TryParse(record, reader.BytesPerCluster, out var parsed) && parsed.HasName) table.Names[number] = new string(parsed.Name);
            });
        }

        int start = MftTreeBuilder.RecordFor(table, volumeRoot, root)
            ?? throw new MftUnavailableException($"{root} isn't reachable by name in the Master File Table (it may sit behind a junction).");
        var built = MftTreeBuilder.Build(table, start, root, configuration, cancellationToken);

        if (NodePath.IsDriveRoot(root))
        {
            var drive = new DriveInfo(root);
            long used = drive.TotalSize - drive.TotalFreeSpace;
            if (built.Root.AllocatedSize > used * UsedSpaceTolerance)
            {
                throw new MftUnavailableException($"The Master File Table adds up to {built.Root.AllocatedSize} bytes on a volume using {used}.");
            }
        }
        return new ScanResult(built.Root, clock.Elapsed, built.FileCount, built.DirectoryCount, InaccessibleCount: 0, ScanMethod.MasterFileTable);
    }
}

/// <summary>
/// The scanner the app uses: the Master File Table when the process is elevated and the folder is on a fixed NTFS
/// volume, the directory walk otherwise — and the directory walk again whenever the table can't be used.
/// </summary>
public sealed class AdaptiveScanner : IDiskScanner
{
    private readonly Func<bool> isElevated;
    private readonly IDiskScanner directories;
    private readonly IDiskScanner masterFileTable;

    public AdaptiveScanner(Func<bool> isElevated, IDiskScanner directories, IDiskScanner masterFileTable)
    {
        this.isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
        this.directories = directories ?? throw new ArgumentNullException(nameof(directories));
        this.masterFileTable = masterFileTable ?? throw new ArgumentNullException(nameof(masterFileTable));
    }

    public async IAsyncEnumerable<ScanEvent> ScanAsync(string root, ScanConfiguration configuration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.AllowMasterFileTable && isElevated() && IsFixedNtfs(root))
        {
            bool fellBack = false;
            await using (var events = masterFileTable.ScanAsync(root, configuration, cancellationToken).GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    ScanEvent current;
                    try
                    {
                        if (!await events.MoveNextAsync().ConfigureAwait(false)) break;
                        current = events.Current;
                    }
                    catch (MftUnavailableException exception)
                    {
                        // Not an error for the person: the directory walk measures the same folder, only slower.
                        Trace.TraceWarning($"Master File Table unavailable, walking directories: {exception.Message}");
                        fellBack = true;
                        break;
                    }
                    yield return current;
                }
            }
            if (!fellBack) yield break;
        }
        await foreach (var scanEvent in directories.ScanAsync(root, configuration, cancellationToken).ConfigureAwait(false))
        {
            yield return scanEvent;
        }
    }

    private static bool IsFixedNtfs(string root)
    {
        try
        {
            string? volume = Path.GetPathRoot(Path.GetFullPath(root));
            if (string.IsNullOrEmpty(volume) || volume.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            var drive = new DriveInfo(volume);
            return drive.IsReady && drive.DriveType == DriveType.Fixed && string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // A volume that can't describe itself is walked like any other folder.
            return false;
        }
    }
}
