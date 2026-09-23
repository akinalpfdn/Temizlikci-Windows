using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;
using Temizlikci.Services.Scanning.Native;

namespace Temizlikci.Services.Scanning;

/// <summary>
/// Walks a folder tree off the UI thread and measures space on disk (DECISIONS 2026-09-23: NtQueryDirectoryFile).
/// </summary>
/// <remarks>
/// Correctness rules, carried over from macOS:
/// <list type="bullet">
/// <item>Sizes are allocation sizes; a hard-linked file is counted once (by NTFS file ID).</item>
/// <item>Junctions, directory symbolic links and mount points (name-surrogate reparse points) are never followed, so
/// nothing is counted twice and no other volume is walked. Cloud placeholders are ordinary folders here.</item>
/// <item>Unreadable folders become inaccessible nodes instead of failing the scan.</item>
/// <item>Paths in <see cref="ScanConfiguration.SkippedPaths"/> are never visited.</item>
/// </list>
/// </remarks>
public sealed class DirectoryScanner : IDiskScanner
{
    private const int BufferSize = 64 * 1024;

    public async IAsyncEnumerable<ScanEvent> ScanAsync(string root, ScanConfiguration configuration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(configuration);
        var channel = Channel.CreateUnbounded<ScanEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var work = Task.Run(async () =>
        {
            try
            {
                var result = await RunAsync(NodePath.Trim(root), configuration, channel.Writer, stop.Token).ConfigureAwait(false);
                channel.Writer.TryWrite(new ScanEvent.Finished(result));
                channel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var scanEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return scanEvent;
            }
        }
        finally
        {
            // The consumer stopped early or was cancelled: stop the walk and wait for it, so no work outlives the scan.
            await stop.CancelAsync().ConfigureAwait(false);
            await work.ConfigureAwait(false);
        }
    }

    private static async Task<ScanResult> RunAsync(string root, ScanConfiguration configuration, ChannelWriter<ScanEvent> events, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var modified = ValidateRoot(root);
        var context = new ScanContext(configuration, cancellationToken);
        using var tickerStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ticker = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(configuration.ProgressInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(tickerStop.Token).ConfigureAwait(false))
                {
                    events.TryWrite(new ScanEvent.Progress(context.Snapshot()));
                }
            }
            catch (OperationCanceledException)
            {
                // Stopping the ticker is how the scan ends; sending one more update after the result would replace it.
            }
        }, CancellationToken.None);

        try
        {
            using var threads = ThreadPoolHeadroom.Reserve(configuration.MaxConcurrency);
            var node = await ScanDirectoryAsync(root, root, modified, 0, topLevel: null, context).ConfigureAwait(false);
            var tally = context.Snapshot();
            return new ScanResult(node, clock.Elapsed, tally.FileCount, tally.DirectoryCount, tally.InaccessibleCount, ScanMethod.DirectoryWalk);
        }
        finally
        {
            await tickerStop.CancelAsync().ConfigureAwait(false);
            await ticker.ConfigureAwait(false);
        }
    }

    /// <summary>Checks the root is a readable folder and returns its modification date.</summary>
    internal static DateTime? ValidateRoot(string root)
    {
        if (!NativeMethods.GetFileAttributesEx(NativeMethods.LongPath(root), 0, out var data))
        {
            int error = Marshal.GetLastPInvokeError();
            throw new ScanException(error == NativeMethods.ErrorAccessDenied ? ScanFailure.RootUnreadable : ScanFailure.RootNotFound, root);
        }
        if ((data.FileAttributes & NativeMethods.FileAttributeDirectory) == 0) throw new ScanException(ScanFailure.RootNotFolder, root);
        using var handle = OpenDirectory(root);
        if (handle.IsInvalid) throw new ScanException(ScanFailure.RootUnreadable, root);
        return data.LastWriteTime == 0 ? null : DateTime.FromFileTimeUtc(data.LastWriteTime);
    }

    // MARK: Walking

    /// <param name="topLevel">The root's child this folder is inside, to report how far each has got; null for the root.</param>
    private static async Task<FileNode> ScanDirectoryAsync(string path, string name, DateTime? modified, int depth, string? topLevel, ScanContext context)
    {
        if (depth >= context.Configuration.ParallelDepth)
        {
            await context.Gate.WaitAsync(context.CancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => ScanDirectorySynchronously(path, name, modified, topLevel, context), context.CancellationToken).ConfigureAwait(false);
            }
            finally
            {
                context.Gate.Release();
            }
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        var listing = Read(path, topLevel, context);
        if (listing is null) return FileNode.Inaccessible(name);

        var tasks = listing.Directories
            .Select(directory => ScanDirectoryAsync(NodePath.Join(path, directory.Name), directory.Name, directory.Modified, depth + 1,
                topLevel ?? directory.Name, context))
            .ToList();
        var subdirectories = new List<FileNode>(tasks.Count);
        await foreach (var finished in Task.WhenEach(tasks).ConfigureAwait(false))
        {
            var node = await finished.ConfigureAwait(false);
            subdirectories.Add(node);
            if (depth == 0) context.RecordCompletedTopLevel(node);
        }
        return FileNode.Directory(name, modified, [.. listing.Files, .. subdirectories]);
    }

    private static FileNode ScanDirectorySynchronously(string path, string name, DateTime? modified, string? topLevel, ScanContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var listing = Read(path, topLevel, context);
        if (listing is null) return FileNode.Inaccessible(name);
        var children = listing.Files;
        foreach (var directory in listing.Directories)
        {
            children.Add(ScanDirectorySynchronously(NodePath.Join(path, directory.Name), directory.Name, directory.Modified, topLevel, context));
        }
        return FileNode.Directory(name, modified, children);
    }

    // MARK: Reading one directory

    private sealed class Listing
    {
        public List<FileNode> Files { get; } = [];
        public List<(string Name, DateTime? Modified)> Directories { get; } = [];
    }

    private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenDirectory(string path) =>
        NativeMethods.CreateFile(NativeMethods.LongPath(path),
            NativeMethods.FileListDirectory | NativeMethods.Synchronize, NativeMethods.FileShareAll, 0,
            NativeMethods.OpenExisting, NativeMethods.FileFlagBackupSemantics | NativeMethods.FileFlagOpenReparsePoint, 0);

    /// <summary>Lists one directory, or returns <c>null</c> (after recording it as inaccessible) when it can't be read.</summary>
    private static unsafe Listing? Read(string path, string? topLevel, ScanContext context)
    {
        using var handle = OpenDirectory(path);
        if (handle.IsInvalid)
        {
            context.RecordInaccessible();
            return null;
        }

        var listing = new Listing();
        var configuration = context.Configuration;
        long threshold = configuration.IndividualFileThreshold;
        int smallCount = 0;
        long smallSize = 0;
        long listedBytes = 0;
        int fileCount = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            fixed (byte* start = buffer)
            {
                bool first = true;
                while (true)
                {
                    int status = NativeMethods.NtQueryDirectoryFile(handle, 0, 0, 0, out _, start, (uint)buffer.Length,
                        NativeMethods.FileIdFullDirectoryInformation, returnSingleEntry: false, 0, restartScan: first);
                    first = false;
                    if (status == NativeMethods.StatusNoMoreFiles) break;
                    if (status < 0)
                    {
                        // Reading failed after the folder opened: keep what was listed and count the folder as partly unread.
                        if (listing.Files.Count == 0 && listing.Directories.Count == 0)
                        {
                            context.RecordInaccessible();
                            return null;
                        }
                        break;
                    }

                    byte* entry = start;
                    while (true)
                    {
                        var info = DirectoryEntry.Read(entry);
                        if (!info.IsDotOrDotDot)
                        {
                            if (info.IsDirectory)
                            {
                                // Junctions, symlinks and mount points stand for another path: never followed, never shown.
                                if (!info.IsNameSurrogate)
                                {
                                    string name = info.Name;
                                    if (configuration.SkippedPaths.Count == 0 || !configuration.SkippedPaths.Contains(NodePath.Join(path, name)))
                                    {
                                        listing.Directories.Add((name, info.LastWriteUtc));
                                    }
                                }
                            }
                            else
                            {
                                long size = info.AllocationSize;
                                if (info.IsWofCompressed) size = CompressedSize(NodePath.Join(path, info.Name), size);
                                if (size > 0 && !context.ClaimFile(info.FileId)) size = 0;
                                listedBytes += size;
                                fileCount++;
                                if (size >= threshold)
                                {
                                    listing.Files.Add(FileNode.File(info.Name, size, info.LastWriteUtc));
                                }
                                else
                                {
                                    smallCount++;
                                    smallSize += size;
                                }
                            }
                        }
                        uint next = *(uint*)entry;
                        if (next == 0) break;
                        entry += next;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (smallCount > 0) listing.Files.Add(FileNode.SmallerFiles(smallCount, smallSize));
        context.RecordDirectory(path, topLevel, fileCount, listedBytes);
        return listing;
    }

    /// <summary>CompactOS keeps a compressed file's data in an alternate stream that directory listings don't count;
    /// the compressed file size API does.</summary>
    private static long CompressedSize(string path, long fallback)
    {
        uint low = NativeMethods.GetCompressedFileSize(NativeMethods.LongPath(path), out uint high);
        if (low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0) return fallback;
        return ((long)high << 32) | low;
    }

    /// <summary>One FILE_ID_FULL_DIR_INFORMATION record.</summary>
    private readonly unsafe ref struct DirectoryEntry
    {
        private readonly byte* entry;

        private DirectoryEntry(byte* entry) => this.entry = entry;

        public static DirectoryEntry Read(byte* entry) => new(entry);

        public long LastWriteTicks => *(long*)(entry + 24);
        public long AllocationSize => *(long*)(entry + 48);
        public uint Attributes => *(uint*)(entry + 56);
        private int NameLength => (int)(*(uint*)(entry + 60) / 2);
        /// <summary>For a reparse point, the EA size field holds the reparse tag instead.</summary>
        private uint ReparseTag => *(uint*)(entry + 64);
        public long FileId => *(long*)(entry + 72);
        private char* NameStart => (char*)(entry + 80);

        public ReadOnlySpan<char> NameSpan => new(NameStart, NameLength);
        public string Name => new(NameStart, 0, NameLength);
        public bool IsDirectory => (Attributes & NativeMethods.FileAttributeDirectory) != 0;
        public bool IsReparsePoint => (Attributes & NativeMethods.FileAttributeReparsePoint) != 0;
        public bool IsNameSurrogate => IsReparsePoint && (ReparseTag & NativeMethods.ReparseTagNameSurrogate) != 0;
        public bool IsWofCompressed => IsReparsePoint && ReparseTag == NativeMethods.ReparseTagWof;
        public bool IsDotOrDotDot => NameSpan is "." or "..";
        public DateTime? LastWriteUtc => LastWriteTicks <= 0 ? null : DateTime.FromFileTimeUtc(LastWriteTicks);
    }
}
