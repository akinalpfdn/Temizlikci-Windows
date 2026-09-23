using System.Runtime.CompilerServices;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Identity;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;
using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Actions;
using Temizlikci.Presentation.Overview;

namespace Temizlikci.Tests.Support;

/// <summary>Replays a fixed list of events, then ends or fails.</summary>
internal sealed class StubScanner(IReadOnlyList<ScanEvent> events, Exception? failure = null) : IDiskScanner
{
    public async IAsyncEnumerable<ScanEvent> ScanAsync(string root, ScanConfiguration configuration, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach (var scanEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return scanEvent;
        }
        if (failure is not null) throw failure;
    }
}

internal sealed class FixedVolume(VolumeUsage usage) : IVolumeInfoProvider
{
    public IReadOnlyList<VolumeDescription> FixedVolumes() => [new(@"C:\", "Windows", "NTFS", true, true)];

    public VolumeDescription? VolumeContaining(string path) => FixedVolumes()[0];

    public VolumeUsage Usage(string path) => usage;
}

internal sealed class RecordingShell : IShell
{
    public List<string> Shown { get; } = [];
    public List<string> Properties { get; } = [];
    public List<Uri> Opened { get; } = [];

    public void ShowInExplorer(string path) => Shown.Add(path);

    public void ShowProperties(string path) => Properties.Add(path);

    public void OpenUri(Uri uri) => Opened.Add(uri);

    public int SystemProtectionOpened { get; private set; }

    public void OpenSystemProtection() => SystemProtectionOpened++;
}

/// <summary>Never touches the real Recycle Bin: records what would have moved.</summary>
internal sealed class StubRecycleBin : IRecycleBin
{
    public List<string> Recycled { get; } = [];
    public List<string> PutBackPaths { get; } = [];
    public RecycleException? Failure { get; set; }
    public RecycleException? PutBackFailure { get; set; }
    public HashSet<string> GoneFromBin { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Opened { get; private set; }

    public RecycledItem MoveToRecycleBin(string path)
    {
        if (Failure is not null) throw Failure;
        Recycled.Add(path);
        return new RecycledItem(path, $@"C:\$Recycle.Bin\S-1\$R{Recycled.Count}");
    }

    public void PutBack(RecycledItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (PutBackFailure is not null) throw PutBackFailure;
        PutBackPaths.Add(item.OriginalPath);
    }

    public IReadOnlyList<RecyclePresence> Presence(IReadOnlyList<RecycledItem> items) =>
        items.Select(item => GoneFromBin.Contains(item.OriginalPath) ? RecyclePresence.Gone : RecyclePresence.Present).ToList();

    public void Open() => Opened++;
}

internal sealed class InMemoryScanCache : IScanCache
{
    private readonly Dictionary<string, byte[]> saved = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> SavedLocations => saved.Keys;

    public void Save(FileNode root, DateTime scannedAtUtc, string locationPath)
    {
        lock (saved) saved[locationPath] = ScanArchive.Encode(root, scannedAtUtc, locationPath);
    }

    public ScanArchive.Archived? Load(string locationPath)
    {
        lock (saved) return saved.TryGetValue(locationPath, out var data) ? ScanArchive.Decode(data) : null;
    }

    public void Remove(string locationPath)
    {
        lock (saved) saved.Remove(locationPath);
    }
}

internal sealed class InMemorySnapshots : ISnapshotStore
{
    private readonly List<ScanSnapshot> all = [];

    public IReadOnlyList<ScanSnapshot> All
    {
        get
        {
            lock (all) return all.ToList();
        }
    }

    public IReadOnlyList<ScanSnapshot> Recent(string locationPath, int limit)
    {
        lock (all)
        {
            return all.Where(snapshot => string.Equals(snapshot.LocationPath, locationPath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(snapshot => snapshot.DateUtc).Take(limit).ToList();
        }
    }

    public void Save(ScanSnapshot snapshot)
    {
        lock (all) all.Add(snapshot);
    }

    public void Prune(string locationPath, int count)
    {
        lock (all)
        {
            var old = all.Where(snapshot => string.Equals(snapshot.LocationPath, locationPath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(snapshot => snapshot.DateUtc).Skip(count).ToList();
            foreach (var snapshot in old) all.Remove(snapshot);
        }
    }
}

internal sealed class NoHiddenSpace : IHiddenSpaceReader
{
    public Task<HiddenSpace> ReadAsync(string volumeRoot, CancellationToken cancellationToken) => Task.FromResult(new HiddenSpace(null, null));
}

internal sealed class NoApps : IInstalledApps, IFileTypeNames
{
    public string? NameForFolder(string folderName) => null;

    public string? NameForPackage(string packageFamilyName) => null;

    public string? Describe(string extension) => null;
}

internal sealed class NoActivity : IProjectActivityReader
{
    public DateTime? LastTouchedUtc(string projectPath, IReadOnlySet<string> ignoringNames) => null;
}

/// <summary>Builds a location model wired to stubs only.</summary>
internal sealed class ModelFixture
{
    public RecordingShell Shell { get; } = new();
    public StubRecycleBin Bin { get; } = new();
    public InMemoryScanCache Cache { get; } = new();
    public InMemorySnapshots Snapshots { get; } = new();
    public UndoHistory Undo { get; } = new();
    public RecycleLedger Ledger { get; }
    public DateTime Now { get; set; } = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    public ModelFixture()
    {
        Ledger = new RecycleLedger(Bin);
    }

    public LocationScanModel Make(
        IReadOnlyList<ScanEvent>? events = null,
        Exception? failure = null,
        bool wholeVolume = false,
        string root = TreeBuilder.RootPath,
        VolumeUsage? usage = null,
        IReadOnlyList<CleanupRule>? rules = null)
    {
        events ??= [new ScanEvent.Finished(new ScanResult(TreeBuilder.Sample(), TimeSpan.FromSeconds(1), 5, 3, 0, ScanMethod.DirectoryWalk))];
        return new LocationScanModel(new ScanLocation(root, "Scan Place", wholeVolume), BuildServices(events, failure, usage, rules));
    }

    /// <summary>The stub services every model from this fixture shares.</summary>
    public LocationServices Services => BuildServices([], null, null, null);

    private LocationServices BuildServices(IReadOnlyList<ScanEvent> events, Exception? failure, VolumeUsage? usage, IReadOnlyList<CleanupRule>? rules)
    {
        var locations = KnownLocations.Sample;
        var markers = new Temizlikci.Tests.Domain.StubMarkers();
        var services = new LocationServices
        {
            Scanner = new StubScanner(events, failure),
            Volumes = new FixedVolume(usage ?? new VolumeUsage(2_000, 800)),
            Shell = Shell,
            RecycleBin = Bin,
            Ledger = Ledger,
            Undo = Undo,
            Rules = new RuleEngine(rules ?? CleanupCatalog.Rules, locations, markers),
            Projects = new ProjectFinder(markers, new NoActivity(), locations),
            Protection = new SystemProtection(locations),
            Identifier = new FolderIdentifier(locations, new NoApps(), new NoApps()),
            Cache = Cache,
            Snapshots = Snapshots,
            HiddenSpace = new NoHiddenSpace(),
            UtcNow = () => Now,
        };
        return services;
    }

    public static async Task<LocationScanModel> Scanned(LocationScanModel model)
    {
        model.StartScan();
        await model.ScanTask!;
        await Settle(model);
        return model;
    }

    /// <summary>Waits for the background work a finished scan starts (matching, history, cache, breakdown).</summary>
    public static async Task Settle(LocationScanModel model)
    {
        for (int round = 0; round < 3; round++)
        {
            foreach (var task in new[] { model.AnalysisTask, model.HistoryTask, model.CacheWriteTask, model.BreakdownTask, model.CacheTask })
            {
                if (task is not null) await task;
            }
        }
    }
}
