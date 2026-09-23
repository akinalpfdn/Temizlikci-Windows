using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;

namespace Temizlikci.Services.Scanning;

/// <summary>Shared, lock-protected state for one scan.</summary>
internal sealed class ScanContext
{
    private const int FileIdShards = 64;

    private readonly Lock progressLock = new();
    private readonly HashSet<long>[] seenFiles;
    private readonly Lock[] seenLocks;
    private readonly List<FileNode> completedTopLevel = [];
    private readonly Dictionary<string, long> measuringTopLevel = new(NodePath.Comparer);
    private long allocatedSize;
    private long fileCount;
    private long directoryCount;
    private long inaccessibleCount;
    private string? currentDirectory;

    public ScanContext(ScanConfiguration configuration, CancellationToken cancellationToken)
    {
        Configuration = configuration;
        CancellationToken = cancellationToken;
        Gate = new SemaphoreSlim(configuration.MaxConcurrency);
        seenFiles = new HashSet<long>[FileIdShards];
        seenLocks = new Lock[FileIdShards];
        for (int index = 0; index < FileIdShards; index++)
        {
            seenFiles[index] = [];
            seenLocks[index] = new Lock();
        }
    }

    public ScanConfiguration Configuration { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>Limits how many directory subtrees are walked at once.</summary>
    public SemaphoreSlim Gate { get; }

    public ScanProgress Snapshot()
    {
        lock (progressLock)
        {
            return new ScanProgress
            {
                AllocatedSize = allocatedSize,
                FileCount = fileCount,
                DirectoryCount = directoryCount,
                InaccessibleCount = inaccessibleCount,
                CurrentDirectory = currentDirectory,
                CompletedTopLevel = completedTopLevel.ToArray(),
                MeasuringTopLevel = new Dictionary<string, long>(measuringTopLevel, NodePath.Comparer),
            };
        }
    }

    public void RecordDirectory(string path, string? topLevel, int files, long bytes)
    {
        lock (progressLock)
        {
            directoryCount++;
            fileCount += files;
            allocatedSize += bytes;
            currentDirectory = path;
            if (topLevel is not null) measuringTopLevel[topLevel] = measuringTopLevel.GetValueOrDefault(topLevel) + bytes;
        }
    }

    public void RecordInaccessible()
    {
        lock (progressLock) inaccessibleCount++;
    }

    public void RecordCompletedTopLevel(FileNode node)
    {
        lock (progressLock)
        {
            completedTopLevel.Add(node);
            measuringTopLevel.Remove(node.Name);
        }
    }

    /// <summary>True the first time a file ID is seen, so a hard-linked file's space is counted once.</summary>
    public bool ClaimFile(long fileId)
    {
        int shard = (int)((ulong)fileId % FileIdShards);
        lock (seenLocks[shard]) return seenFiles[shard].Add(fileId);
    }
}

/// <summary>
/// Directory reads block a thread each. The thread pool injects threads slowly when all of its minimum are blocked, so a
/// scan raises the minimum for its duration and restores it afterwards.
/// </summary>
internal sealed class ThreadPoolHeadroom : IDisposable
{
    private static readonly Lock Sync = new();
    private static int holders;
    private static int savedWorkers;
    private static int savedIo;

    private ThreadPoolHeadroom()
    {
    }

    public static ThreadPoolHeadroom Reserve(int concurrency)
    {
        lock (Sync)
        {
            if (holders++ == 0)
            {
                ThreadPool.GetMinThreads(out savedWorkers, out savedIo);
                ThreadPool.SetMinThreads(Math.Max(savedWorkers, concurrency + Environment.ProcessorCount), savedIo);
            }
        }
        return new ThreadPoolHeadroom();
    }

    public void Dispose()
    {
        lock (Sync)
        {
            if (--holders == 0) ThreadPool.SetMinThreads(savedWorkers, savedIo);
        }
    }
}
