using CommunityToolkit.Mvvm.ComponentModel;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Identity;
using Temizlikci.Domain.Layout;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;
using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Actions;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.Presentation.Overview;

public enum SortColumn
{
    Name,
    Size,
}

/// <summary>
/// Scan, navigation, selection, search, recycling, history and cleanup matches for one location (a drive, Home, or a
/// chosen folder). A port of the macOS LocationScanModel: every public member runs on the UI thread; tree walks, disk
/// reads and writes run on the thread pool and come back with their results.
/// </summary>
public sealed partial class LocationScanModel : ObservableObject, IDisposable
{
    /// <summary>Search stops after this many matches so typing stays responsive on large trees.</summary>
    public const int SearchResultLimit = 500;

    private readonly LocationServices services;
    private List<IReadOnlyList<NodeRef>> backStack = [];
    private List<IReadOnlyList<NodeRef>> forwardStack = [];
    /// <summary>For every node reachable from the open folder through the chart or search: its chain of folders,
    /// starting with the open folder.</summary>
    private Dictionary<string, IReadOnlyList<NodeRef>> chains = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, int> slots = new Dictionary<string, int>();
    private Dictionary<string, CleanupMatch> matchesById = new(StringComparer.Ordinal);
    private CancellationTokenSource? scanCancellation;
    /// <summary>Incremented for every scan started or stopped, so a cancelled scan's leftovers are ignored.</summary>
    private int scanGeneration;
    private bool recordsHistoryAfterMatching;
    private string searchText = string.Empty;
    private SortColumn sortColumn = SortColumn.Size;
    private bool sortDescending = true;
    private string? hoveredId;
    private bool isHighlightingReclaimable;

    public LocationScanModel(ScanLocation location, LocationServices services)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public ScanLocation Location { get; }

    private string RootPath => NodePath.Trim(Location.Path);

    // MARK: State

    public ScanPhase Phase { get; private set; } = ScanPhase.Idle;
    public string? FailureMessage { get; private set; }
    public string? FailureSuggestion { get; private set; }
    public ScanProgress Progress { get; private set; } = new();
    public ScanResult? Result { get; private set; }
    public VolumeUsage? Usage { get; private set; }
    public DateTime? ScannedAtUtc { get; private set; }
    /// <summary>True while a scan refreshes a tree that is already on screen.</summary>
    public bool IsRefreshing { get; private set; }
    /// <summary>What the chart and list show: the scan result plus unattributed space, or a partial tree while scanning.</summary>
    public FileNode? Tree { get; private set; }
    /// <summary>Folders from the tree's root down to the folder shown in the chart.</summary>
    public IReadOnlyList<NodeRef> Path { get; private set; } = [];
    public IReadOnlyList<SunburstSegment> Segments { get; private set; } = [];
    public IReadOnlyList<NodeRef> Rows { get; private set; } = [];
    public NodeRef? Selection { get; private set; }
    /// <summary>The most recent Move to Recycle Bin, shown as a confirmation with Undo until dismissed.</summary>
    public RecycleRecord? LastRecycled { get; private set; }
    public ActionError? ActionError { get; private set; }
    /// <summary>True when the tree could not be updated in place and a rescan would show sizes more accurately.</summary>
    public bool IsOutdated { get; private set; }
    /// <summary>Developer artifacts recognized in the current tree, largest first.</summary>
    public IReadOnlyList<CleanupMatch> CleanupMatches { get; private set; } = [];
    public IReadOnlyList<LargeFile> LargeFiles { get; private set; } = [];
    public IReadOnlyList<DeveloperProject> Projects { get; private set; } = [];
    /// <summary>Top-level folders still being scanned, shown with their size so far.</summary>
    public IReadOnlySet<string> MeasuringIds { get; private set; } = new HashSet<string>();
    public SpaceBreakdown? SpaceBreakdown { get; private set; }
    /// <summary>What changed since the previous scan of this location; null for a first scan.</summary>
    public GrowthReport? Growth { get; private set; }
    /// <summary>True while <see cref="Growth"/> comes from saved scans rather than the scan on screen.</summary>
    public bool GrowthIsFromSavedScans { get; private set; }

    // Background work, exposed so tests (and views) can wait for it.
    public Task? ScanTask { get; private set; }
    public Task? AnalysisTask { get; private set; }
    public Task? CacheTask { get; private set; }
    public Task? CacheWriteTask { get; private set; }
    public Task? HistoryTask { get; private set; }
    public Task? BreakdownTask { get; private set; }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (!SetProperty(ref searchText, value ?? string.Empty)) return;
            RefreshRows();
            Changed();
        }
    }

    public SortColumn SortColumn
    {
        get => sortColumn;
        set
        {
            if (!SetProperty(ref sortColumn, value)) return;
            RefreshRows();
            Changed();
        }
    }

    public bool SortDescending
    {
        get => sortDescending;
        set
        {
            if (!SetProperty(ref sortDescending, value)) return;
            RefreshRows();
            Changed();
        }
    }

    /// <summary>The segment under the pointer. Changes often, so it raises only its own notification.</summary>
    public string? HoveredId
    {
        get => hoveredId;
        set
        {
            if (!SetProperty(ref hoveredId, value)) return;
            OnPropertyChanged(nameof(FocusNode));
        }
    }

    public bool IsHighlightingReclaimable
    {
        get => isHighlightingReclaimable;
        set
        {
            if (!SetProperty(ref isHighlightingReclaimable, value && CanHighlightReclaimable)) return;
            Changed();
        }
    }

    // MARK: Derived state

    public NodeRef? CurrentFolder => Path.Count > 0 ? Path[^1] : null;
    public bool IsScanning => Phase == ScanPhase.Scanning;
    public bool HasResult => Phase == ScanPhase.Finished;
    public bool CanGoBack => HasResult && backStack.Count > 0;
    public bool CanGoForward => HasResult && forwardStack.Count > 0;
    public bool CanGoUp => HasResult && Path.Count > 1;
    public bool CanHighlightReclaimable => HasResult && CleanupMatches.Count > 0;

    /// <summary>What the chart center describes: the hovered item, else the selection, else the open folder.</summary>
    public NodeRef? FocusNode => (HoveredId is { } hovered ? NodeById(hovered) : null) ?? Selection ?? CurrentFolder;

    public NodeRef? NodeById(string id)
    {
        if (CurrentFolder is { } folder && folder.Id == id) return folder;
        return chains.TryGetValue(id, out var chain) ? chain[^1] : null;
    }

    /// <summary>Any node of the current tree by ID, including ones outside the open folder (insight views show items
    /// from all over the disk).</summary>
    public NodeRef? NodeAnywhere(string id)
    {
        if (NodeById(id) is { } known) return known;
        if (matchesById.TryGetValue(id, out var match)) return new NodeRef(match.Node, match.Path);
        if (Projects.FirstOrDefault(project => project.Id == id) is { } project) return new NodeRef(project.Node, project.Path);
        if (LargeFiles.FirstOrDefault(file => file.Id == id) is { } file) return new NodeRef(file.Node, file.Path);
        return null;
    }

    // MARK: Scanning

    /// <param name="refreshing">True replaces a tree that is already on screen; the chart, list and open folder stay
    /// until the new scan finishes.</param>
    public void StartScan(bool refreshing = false)
    {
        scanCancellation?.Cancel();
        scanCancellation?.Dispose();
        scanCancellation = new CancellationTokenSource();
        int generation = ++scanGeneration;
        // Capacity only adds the unmeasured and unattributed segments; without it the scan is still correct.
        Usage = Location.IsWholeVolume ? TryUsage() : null;
        IsRefreshing = refreshing && HasResult;
        if (!IsRefreshing)
        {
            Phase = ScanPhase.Scanning;
            Progress = new ScanProgress();
            Result = null;
            ScannedAtUtc = null;
            Growth = null;
            FailureMessage = null;
            FailureSuggestion = null;
            Show(null);
        }
        Changed();
        ScanTask = RunScanAsync(generation, scanCancellation.Token);
    }

    private async Task RunScanAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var scanEvent in services.Scanner.ScanAsync(RootPath, services.Configuration, cancellationToken))
            {
                // Events a cancelled scan still had buffered must not reach the screen.
                if (scanGeneration != generation) return;
                switch (scanEvent)
                {
                    // A refresh leaves the previous tree on screen, so partial results are ignored.
                    case ScanEvent.Progress progress when !IsRefreshing:
                        Apply(progress.Snapshot);
                        break;
                    case ScanEvent.Finished finished:
                        Finish(finished.Result);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping is intentional; StopScan already reset the state.
        }
        catch (Exception exception) when (exception is ScanException or IOException or UnauthorizedAccessException)
        {
            if (scanGeneration == generation) Fail(exception);
        }
    }

    public void StopScan()
    {
        scanCancellation?.Cancel();
        scanGeneration++;
        ScanTask = null;
        Phase = ScanPhase.Idle;
        IsRefreshing = false;
        Show(null);
        Changed();
    }

    private void Apply(ScanProgress snapshot)
    {
        // A late update must never replace a finished result: it would turn Other Used Space back into "Not Scanned Yet".
        if (Phase != ScanPhase.Scanning) return;
        Progress = snapshot;
        var children = snapshot.CompletedTopLevel.ToList();
        // Folders still being read appear with what has been measured so far and grow on every update.
        var finished = children.Select(child => child.Name).ToHashSet(NodePath.Comparer);
        var measuring = snapshot.MeasuringTopLevel.Where(entry => entry.Value > 0 && !finished.Contains(entry.Key)).ToList();
        MeasuringIds = measuring.Select(entry => NodePath.Join(RootPath, entry.Key)).ToHashSet(StringComparer.Ordinal);
        children.AddRange(measuring.Select(entry => FileNode.Measuring(entry.Key, entry.Value)));
        long measured = children.Sum(child => child.AllocatedSize);
        if (Usage is { } usage && usage.UsedCapacity > measured) children.Add(FileNode.Pending(usage.UsedCapacity - measured));
        var partial = FileNode.Directory(RootPath, null, children);
        Tree = partial;
        Path = [NodeRef.Root(partial)];
        RefreshLayout();
        RefreshRows();
        Changed();
    }

    private void Finish(ScanResult finished)
    {
        string? openFolderPath = IsRefreshing ? CurrentFolder?.Path : null;
        Result = finished;
        ScannedAtUtc = services.UtcNow();
        var root = finished.Root;
        if (Usage is { } usage)
        {
            long unattributed = usage.Unattributed(root.AllocatedSize);
            if (unattributed > 0)
            {
                root = root.Adding(FileNode.Unattributed(unattributed));
                LoadSpaceBreakdown(unattributed, finished.InaccessibleCount > 0, finished.Method);
            }
        }
        Phase = ScanPhase.Finished;
        IsRefreshing = false;
        MeasuringIds = new HashSet<string>();
        recordsHistoryAfterMatching = true;
        Show(root);
        // A refresh shouldn't move the person: go back to the folder they were looking at.
        if (openFolderPath is not null && !NodePath.Comparer.Equals(openFolderPath, RootPath)) ShowItem(openFolderPath);
        SaveToCache(root);
        Changed();
    }

    private void Fail(Exception exception)
    {
        var (message, suggestion) = ErrorText.For(exception);
        Phase = ScanPhase.Failed;
        FailureMessage = message;
        FailureSuggestion = suggestion;
        IsRefreshing = false;
        Show(null);
        Changed();
    }

    private VolumeUsage? TryUsage()
    {
        try
        {
            return services.Volumes.Usage(Location.Path);
        }
        catch (IOException)
        {
            // Without capacity figures the chart just has no Other Used Space segment.
            return null;
        }
    }

    private void Show(FileNode? root)
    {
        Tree = root;
        Path = root is null ? [] : [NodeRef.Root(root)];
        backStack = [];
        forwardStack = [];
        Selection = null;
        hoveredId = null;
        searchText = string.Empty;
        LastRecycled = null;
        IsOutdated = false;
        RefreshLayout();
        RefreshRows();
        RefreshAnalysis();
    }

    // MARK: Cached scans

    /// <summary>Shows the last scan of this location, if one was saved, without reading the disk.</summary>
    public void LoadCachedScan()
    {
        if (HasResult || Phase != ScanPhase.Idle || CacheTask is not null) return;
        CacheTask = LoadCachedScanAsync();
    }

    private async Task LoadCachedScanAsync()
    {
        var cache = services.Cache;
        string path = RootPath;
        var archived = await Task.Run(() =>
        {
            try
            {
                return cache.Load(path);
            }
            catch (Exception exception) when (exception is IOException or ScanArchive.ArchiveException or UnauthorizedAccessException or InvalidDataException)
            {
                // The cache is a convenience: a missing or unreadable file just means scanning instead.
                return null;
            }
        }).ConfigureAwait(true);
        CacheTask = null;
        if (archived is null || HasResult || Phase != ScanPhase.Idle)
        {
            Changed();
            return;
        }
        Usage = Location.IsWholeVolume ? TryUsage() : null;
        ScannedAtUtc = archived.ScannedAtUtc;
        Phase = ScanPhase.Finished;
        recordsHistoryAfterMatching = false;
        Show(archived.Root);
        if (Usage is { } usage)
        {
            long unattributed = usage.Unattributed(archived.Root.Children.Where(child => child.Kind != NodeKind.Unattributed).Sum(child => child.AllocatedSize));
            if (unattributed > 0) LoadSpaceBreakdown(unattributed, hasUnreadable: archived.Root.Children.Any(child => child.Kind == NodeKind.Inaccessible), ScanMethod.DirectoryWalk);
        }
        LoadSavedGrowth();
        Changed();
    }

    /// <summary>True when the tree on screen is older than <paramref name="period"/> and a refresh is worth starting.</summary>
    public bool NeedsRefresh(RefreshPeriod period, DateTime nowUtc)
    {
        if (period.Interval() is not { } interval || ScannedAtUtc is not { } scannedAt || !HasResult) return false;
        return nowUtc - scannedAt >= interval;
    }

    private void SaveToCache(FileNode root)
    {
        var cache = services.Cache;
        string path = RootPath;
        var date = ScannedAtUtc ?? services.UtcNow();
        CacheWriteTask = Task.Run(() =>
        {
            try
            {
                cache.Save(root, date, path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Writing the cache is a convenience; a failure must never disturb the scan.
                System.Diagnostics.Trace.TraceWarning($"Saving the scan of {path} failed: {exception.Message}");
            }
        });
    }

    // MARK: Cleanup rules, large files, projects

    /// <summary>The rule match that covers <paramref name="node"/>: its own, or the closest matched folder above it.</summary>
    public CleanupMatch? CleanupMatchFor(NodeRef node) =>
        MatchAlong(AbsoluteIdPath(node) ?? [node.Id]);

    private CleanupMatch? MatchAlong(IReadOnlyList<string> ids)
    {
        for (int index = ids.Count - 1; index >= 0; index--)
        {
            if (matchesById.TryGetValue(ids[index], out var match)) return match;
        }
        return null;
    }

    private void RefreshAnalysis()
    {
        if (!HasResult || Tree is not { } root)
        {
            CleanupMatches = [];
            matchesById = new Dictionary<string, CleanupMatch>(StringComparer.Ordinal);
            LargeFiles = [];
            Projects = [];
            isHighlightingReclaimable = false;
            AnalysisTask = null;
            return;
        }
        AnalysisTask = AnalyzeAsync(root);
    }

    /// <summary>Walks the whole tree (rules, largest files, projects) and reads a little from disk for the projects, so
    /// it runs off the UI thread.</summary>
    private async Task AnalyzeAsync(FileNode root)
    {
        var rules = services.Rules;
        var finder = services.Projects;
        var (matches, largest, projects) = await Task.Run(() =>
        {
            var found = rules.Matches(root);
            return (found, LargeFileFinder.Largest(root), finder.Projects(root, found));
        }).ConfigureAwait(true);
        if (!ReferenceEquals(Tree, root)) return;
        LargeFiles = largest;
        Projects = projects;
        CleanupMatches = matches.OrderByDescending(match => match.Node.AllocatedSize).ToList();
        matchesById = new Dictionary<string, CleanupMatch>(StringComparer.Ordinal);
        foreach (var match in matches) matchesById.TryAdd(match.Id, match);
        if (matches.Count == 0) isHighlightingReclaimable = false;
        if (recordsHistoryAfterMatching)
        {
            recordsHistoryAfterMatching = false;
            RecordHistory(root, matches.Select(match => match.Path).ToHashSet(NodePath.Comparer));
        }
        Changed();
    }

    /// <summary>The fill of a segment, honoring Highlight Reclaimable.</summary>
    public SegmentFill DisplayFill(SunburstSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!IsHighlightingReclaimable) return segment.Fill;
        if (segment.NodeId is not { } id || NodeById(id) is not { } node || CleanupMatchFor(node) is not { } match) return SegmentFill.Dimmed;
        return SegmentFill.ForSafety(match.Rule.Safety);
    }

    public SegmentFill DisplayFill(NodeRef node)
    {
        if (!IsHighlightingReclaimable) return Fill(node);
        return CleanupMatchFor(node) is { } match ? SegmentFill.ForSafety(match.Rule.Safety) : SegmentFill.Dimmed;
    }

    public IReadOnlyList<DeveloperProject> StaleProjects(StalePeriod period, DateTime nowUtc) =>
        Projects.Where(project => project.IsStale(nowUtc, period)).ToList();

    public DeveloperProject? ProjectById(string id) => Projects.FirstOrDefault(project => project.Id == id);

    public FolderIdentity? Identity(NodeRef node) => services.Identifier.Identify(node.Node, node.Path);

    // MARK: Other Used Space

    private void LoadSpaceBreakdown(long unattributed, bool hasUnreadable, ScanMethod method)
    {
        var reader = services.HiddenSpace;
        string volume = System.IO.Path.GetPathRoot(Location.Path) ?? Location.Path;
        SpaceBreakdown = null;
        BreakdownTask = LoadSpaceBreakdownAsync(reader, volume, unattributed, hasUnreadable, method);
    }

    private async Task LoadSpaceBreakdownAsync(IHiddenSpaceReader reader, string volume, long unattributed, bool hasUnreadable, ScanMethod method)
    {
        HiddenSpace hidden;
        try
        {
            hidden = await reader.ReadAsync(volume, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The breakdown only explains; without it the segment is still "Other Used Space".
            hidden = new HiddenSpace(null, null);
        }
        // The Master File Table scan already lists NTFS's own files in the tree; counting them again would double them.
        if (method == ScanMethod.MasterFileTable) hidden = hidden with { FileSystemMetadata = null };
        SpaceBreakdown = SpaceBreakdown.Make(unattributed, hidden, hasUnreadable);
        Changed();
    }

    // MARK: History

    /// <summary>Compares the two newest saved scans of this location, so history survives restarting the app. Only for a
    /// location that has not been scanned in this session; a scan replaces the report.</summary>
    public void LoadSavedGrowth()
    {
        if (Result is not null || Growth is not null || HistoryTask is { IsCompleted: false }) return;
        var store = services.Snapshots;
        string path = RootPath;
        HistoryTask = LoadSavedGrowthAsync(store, path);
    }

    private async Task LoadSavedGrowthAsync(ISnapshotStore store, string path)
    {
        var report = await Task.Run(() =>
        {
            try
            {
                var saved = store.Recent(path, 2);
                return saved.Count == 2 ? GrowthReport.Compare(saved[1], saved[0]) : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                // History is a convenience: an unreadable snapshot means no comparison, not an error.
                return null;
            }
        }).ConfigureAwait(true);
        if (Result is not null) return;
        Growth = report;
        GrowthIsFromSavedScans = report is not null;
        Changed();
    }

    public GrowthChange? GrowthFor(NodeRef node)
    {
        // Only real items have a history: "Not Scanned Yet" and "Other Used Space" share the root's path.
        if (node.Node.Kind is not (NodeKind.Directory or NodeKind.File)) return null;
        return Growth?.ChangeFor(node.Path);
    }

    private void RecordHistory(FileNode root, IReadOnlySet<string> matchPaths)
    {
        var store = services.Snapshots;
        string path = RootPath;
        var date = ScannedAtUtc ?? services.UtcNow();
        int kept = services.SnapshotsKept;
        HistoryTask = RecordHistoryAsync(store, root, path, date, matchPaths, kept);
    }

    private async Task RecordHistoryAsync(ISnapshotStore store, FileNode root, string path, DateTime date, IReadOnlySet<string> matchPaths, int kept)
    {
        var report = await Task.Run(() =>
        {
            ScanSnapshot? previous = null;
            try
            {
                previous = store.Latest(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                // An unreadable older snapshot only means no comparison this time.
                System.Diagnostics.Trace.TraceWarning($"Reading history of {path} failed: {exception.Message}");
            }
            var recording = new HashSet<string>(matchPaths, NodePath.Comparer);
            if (previous is not null) recording.UnionWith(previous.Sizes.Keys);
            var current = SnapshotBuilder.Snapshot(root, path, date, recording);
            try
            {
                store.Save(current);
                store.Prune(path, kept);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // History is a convenience: failing to write it must never affect the scan itself.
                System.Diagnostics.Trace.TraceWarning($"Saving history of {path} failed: {exception.Message}");
            }
            return previous is null ? null : GrowthReport.Compare(previous, current);
        }).ConfigureAwait(true);
        if (!ReferenceEquals(Tree, root)) return;
        Growth = report;
        GrowthIsFromSavedScans = false;
        Changed();
    }

    /// <summary>Opens the folder containing <paramref name="path"/> and selects the item, for "Show in Chart".</summary>
    public void ShowItem(string path)
    {
        if (!HasResult || Tree is not { } root || root.Locate(path) is not { Count: >= 2 } chain) return;
        var target = chain[^1];
        bool opensAsFolder = target.Node.Kind == NodeKind.Directory && target.Node.Children.Count > 0;
        Navigate(opensAsFolder ? chain : chain.Take(chain.Count - 1).ToList());
        Selection = opensAsFolder ? null : target;
        Changed();
    }

    // MARK: Recycle Bin

    /// <summary>Real files and folders below the scanned location, except items a rule or the system protects.</summary>
    public bool CanRecycle(NodeRef? node)
    {
        if (!HasResult || node is not { } item || Tree is null || item.Id == RootPath) return false;
        if (item.Node.Kind is not (NodeKind.Directory or NodeKind.File)) return false;
        if (services.Protection.IsProtected(item.Path)) return false;
        return (CleanupMatchFor(item)?.Rule.Safety ?? SafetyLevel.Safe) == SafetyLevel.Safe;
    }

    public bool CanRecycle(LargeFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return HasResult && !services.Protection.IsProtected(file.Path) && (MatchAlong(file.IdPath)?.Rule.Safety ?? SafetyLevel.Safe) == SafetyLevel.Safe;
    }

    /// <summary>Moves <paramref name="node"/> to the Recycle Bin, updates the tree without rescanning, and registers
    /// Undo. No confirmation: the action is undoable.</summary>
    public void Recycle(NodeRef? node)
    {
        if (node is not { } item || AbsoluteIdPath(item) is not { } ids) return;
        Recycle(item, ids);
    }

    /// <summary>From the Developer view, where the item may not be visible in the chart.</summary>
    public void Recycle(CleanupMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (match.Rule.Safety != SafetyLevel.Safe) return;
        Recycle(new NodeRef(match.Node, match.Path), match.IdPath);
        // Drop the row right away; re-matching the whole tree takes a moment on a full drive.
        if (LastRecycled?.Item.OriginalPath == match.Path)
        {
            CleanupMatches = CleanupMatches.Where(existing => existing.Id != match.Id).ToList();
            matchesById.Remove(match.Id);
            Changed();
        }
    }

    public void Recycle(LargeFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!CanRecycle(file)) return;
        Recycle(new NodeRef(file.Node, file.Path), file.IdPath);
        if (LastRecycled?.Item.OriginalPath == file.Path)
        {
            LargeFiles = LargeFiles.Where(existing => existing.Id != file.Id).ToList();
            Changed();
        }
    }

    /// <summary>Items dragged onto the sidebar's Recycle Bin: only items of this scan that nothing protects.</summary>
    public bool RecycleDropped(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        bool moved = false;
        foreach (var path in paths)
        {
            if (!HasResult || Tree?.Locate(path) is not { Count: >= 2 } chain) continue;
            var target = chain[^1];
            var ids = chain.Select(item => item.Id).ToList();
            if (target.Node.Kind is not (NodeKind.Directory or NodeKind.File)) continue;
            if ((MatchAlong(ids)?.Rule.Safety ?? SafetyLevel.Safe) != SafetyLevel.Safe || services.Protection.IsProtected(target.Path)) continue;
            Recycle(target, ids);
            moved |= LastRecycled?.Item.OriginalPath == target.Path;
        }
        return moved;
    }

    private void Recycle(NodeRef item, IReadOnlyList<string> ids)
    {
        if (!HasResult || Tree is not { } tree || item.Id == RootPath) return;
        if (item.Node.Kind is not (NodeKind.Directory or NodeKind.File)) return;
        if (services.Protection.IsProtected(item.Path) || (MatchAlong(ids)?.Rule.Safety ?? SafetyLevel.Safe) != SafetyLevel.Safe) return;
        RecycledItem recycled;
        try
        {
            recycled = services.RecycleBin.MoveToRecycleBin(item.Path);
        }
        catch (RecycleException exception)
        {
            Report(exception);
            return;
        }
        var record = new RecycleRecord(Guid.NewGuid(), item.Node, recycled, services.UtcNow(), RootPath, ids.Take(ids.Count - 1).ToList());
        if (tree.RemovingDescendant(ids) is { } updated)
        {
            ReplaceTree(updated);
        }
        else
        {
            IsOutdated = true;
        }
        services.Ledger.Add(record);
        LastRecycled = record;
        services.Undo.Register(L10n.RecycleAction, () => PutBack(record));
        Changed();
    }

    /// <summary>Returns a recycled item to its folder and to the tree.</summary>
    public void PutBack(RecycleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            services.RecycleBin.PutBack(record.Item);
        }
        catch (RecycleException exception)
        {
            Report(exception);
            return;
        }
        services.Ledger.Remove(record);
        if (LastRecycled?.Id == record.Id) LastRecycled = null;
        if (Tree is { } tree && NodePath.Comparer.Equals(record.LocationPath, RootPath)
            && tree.InsertingDescendant(record.Node, record.ParentIdPath) is { } updated)
        {
            ReplaceTree(updated);
        }
        else
        {
            IsOutdated = HasResult;
        }
        Changed();
    }

    public void DismissRecycleConfirmation()
    {
        LastRecycled = null;
        Changed();
    }

    public void DismissError()
    {
        ActionError = null;
        Changed();
    }

    private void Report(Exception exception)
    {
        var (message, suggestion) = ErrorText.For(exception);
        ActionError = new ActionError(message, suggestion);
        Changed();
    }

    /// <summary>IDs from the tree root down to <paramref name="node"/>.</summary>
    private List<string>? AbsoluteIdPath(NodeRef node)
    {
        var pathIds = Path.Select(item => item.Id).ToList();
        if (CurrentFolder is { } folder && node.Id == folder.Id) return pathIds;
        if (!chains.TryGetValue(node.Id, out var chain)) return null;
        pathIds.AddRange(chain.Skip(1).Select(item => item.Id));
        return pathIds;
    }

    private void ReplaceTree(FileNode updated)
    {
        var root = RebalancingUnattributed(updated);
        var oldPath = Path.Select(item => item.Id).ToList();
        Tree = root;
        Path = root.NodesAlong(oldPath);
        backStack = [];
        forwardStack = [];
        Selection = null;
        hoveredId = null;
        RefreshLayout();
        RefreshRows();
        RefreshAnalysis();
        // Keep the saved scan in step with the tree, so reopening the app doesn't list recycled items.
        SaveToCache(root);
    }

    /// <summary>Marks the result as outdated after an outside tool (wsl, DISM) changed the disk.</summary>
    public void MarkOutdated()
    {
        if (!HasResult) return;
        IsOutdated = true;
        Changed();
    }

    /// <summary>Recycled items still use space until the Recycle Bin is emptied, so used capacity doesn't change:
    /// whatever leaves the scanned folders moves into Other Used Space.</summary>
    private FileNode RebalancingUnattributed(FileNode root)
    {
        if (Usage is not { } usage) return root;
        var scanned = root.WithChildren(root.Children.Where(child => child.Kind != NodeKind.Unattributed));
        long other = usage.Unattributed(scanned.AllocatedSize);
        return other > 0 ? scanned.Adding(FileNode.Unattributed(other)) : scanned;
    }

    // MARK: Navigation

    public void Open(NodeRef node)
    {
        if (!HasResult || node.Node.Kind != NodeKind.Directory || node.Node.Children.Count == 0) return;
        if (CurrentFolder is { } folder && folder.Id == node.Id) return;
        if (!chains.TryGetValue(node.Id, out var chain)) return;
        Navigate([.. Path, .. chain.Skip(1)]);
        Selection = null;
        Changed();
    }

    public void OpenSelection()
    {
        if (Selection is { } selection) Open(selection);
    }

    public void GoUp()
    {
        if (!CanGoUp || CurrentFolder is not { } leaving) return;
        Navigate(Path.Take(Path.Count - 1).ToList());
        Selection = chains.TryGetValue(leaving.Id, out var chain) ? chain[^1] : leaving;
        Changed();
    }

    public void GoBack()
    {
        if (!CanGoBack) return;
        forwardStack.Add(Path);
        Path = backStack[^1];
        backStack.RemoveAt(backStack.Count - 1);
        DidNavigate();
    }

    public void GoForward()
    {
        if (!CanGoForward) return;
        backStack.Add(Path);
        Path = forwardStack[^1];
        forwardStack.RemoveAt(forwardStack.Count - 1);
        DidNavigate();
    }

    public void GoToAncestor(int index)
    {
        if (!HasResult || index < 0 || index >= Path.Count - 1) return;
        Navigate(Path.Take(index + 1).ToList());
        Changed();
    }

    private void Navigate(IReadOnlyList<NodeRef> newPath)
    {
        if (newPath.Select(item => item.Id).SequenceEqual(Path.Select(item => item.Id))) return;
        backStack.Add(Path);
        forwardStack.Clear();
        Path = newPath;
        DidNavigate();
    }

    private void DidNavigate()
    {
        Selection = null;
        hoveredId = null;
        searchText = string.Empty;
        RefreshLayout();
        RefreshRows();
        Changed();
    }

    // MARK: Selection

    public void Select(NodeRef? node)
    {
        Selection = node;
        Changed();
    }

    public void SelectById(string? id) => Select(id is null ? null : NodeById(id));

    /// <summary>Moves the selection among its siblings, wrapping around. Selects the first item when nothing is selected.</summary>
    public void SelectSibling(int offset)
    {
        if (Selection is not { } current || !chains.TryGetValue(current.Id, out var chain) || chain.Count < 2)
        {
            SelectFirstChild(CurrentFolder);
            return;
        }
        var parentChain = chain.Take(chain.Count - 1).ToList();
        var siblings = VisibleChildren(chain[^2]);
        int index = siblings.FindIndex(sibling => sibling.Id == current.Id);
        if (index < 0 || siblings.Count == 0) return;
        var next = siblings[((index + offset) % siblings.Count + siblings.Count) % siblings.Count];
        chains[next.Id] = [.. parentChain, next];
        Select(next);
    }

    /// <summary>Moves the selection one ring inward (to its parent), staying inside the open folder.</summary>
    public void SelectParentRing()
    {
        if (Selection is not { } current || !chains.TryGetValue(current.Id, out var chain) || chain.Count <= 2) return;
        Select(chain[^2]);
    }

    /// <summary>Moves the selection one ring outward (to its largest child), within the drawn rings.</summary>
    public void SelectChildRing()
    {
        if (Selection is not { } current || !chains.TryGetValue(current.Id, out var chain) || chain.Count > SunburstLayout.RingCount)
        {
            if (Selection is null) SelectFirstChild(CurrentFolder);
            return;
        }
        var child = VisibleChildren(current).FirstOrDefault();
        if (child.Node is null) return;
        chains[child.Id] = [.. chain, child];
        Select(child);
    }

    private void SelectFirstChild(NodeRef? folder)
    {
        if (folder is not { } parent || VisibleChildren(parent).FirstOrDefault() is not { Node: not null } first) return;
        chains[first.Id] = [parent, first];
        Select(first);
    }

    private static List<NodeRef> VisibleChildren(NodeRef node) =>
        node.Children.Where(child => child.Node.AllocatedSize > 0).ToList();

    // MARK: Presentation

    public string Title(NodeRef node)
    {
        if (node.Id == RootPath) return Location.DisplayName;
        return node.Node.Kind switch
        {
            NodeKind.SmallerFiles => L10n.NodeSmallerFiles(Formatting.Format.Count(node.Node.FileCount)),
            NodeKind.Unattributed => L10n.NodeUnattributed,
            NodeKind.Pending => L10n.NodePending,
            _ => node.Node.Name,
        };
    }

    public string Title(SunburstSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.IsMerged) return L10n.NodeMergedItems;
        return segment.NodeId is { } id && NodeById(id) is { } node ? Title(node) : segment.Name;
    }

    /// <summary>The chart color role for a list row, so rows and segments always match.</summary>
    public SegmentFill Fill(NodeRef node)
    {
        switch (node.Node.Kind)
        {
            case NodeKind.Unattributed: return SegmentFill.UnattributedHatch;
            case NodeKind.Inaccessible: return SegmentFill.InaccessibleHatch;
            case NodeKind.Pending: return SegmentFill.Pending;
            case NodeKind.SmallerFiles: return SegmentFill.Neutral;
        }
        if (!chains.TryGetValue(node.Id, out var chain) || chain.Count < 2 || !slots.TryGetValue(chain[1].Id, out int slot)) return SegmentFill.Neutral;
        return SegmentFill.ForSlot(slot, chain.Count - 1);
    }

    public static double? Share(FileNode node, FileNode? container)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (container is null || container.AllocatedSize <= 0) return null;
        return node.AllocatedSize / (double)container.AllocatedSize;
    }

    /// <summary>The folder that contains <paramref name="node"/>, if it is visible from the open folder.</summary>
    public NodeRef? Parent(NodeRef node)
    {
        if (CurrentFolder is { } folder && node.Id == folder.Id) return Path.Count > 1 ? Path[^2] : null;
        return chains.TryGetValue(node.Id, out var chain) && chain.Count >= 2 ? chain[^2] : null;
    }

    /// <summary>The path Explorer can show for an item; null for space that isn't a place (Other Used Space).</summary>
    public static string? ActionablePath(NodeRef? node) => node is { } item && item.Node.Kind is not (NodeKind.Unattributed or NodeKind.Pending)
        ? item.Path
        : null;

    public void ShowInExplorer(NodeRef? node)
    {
        if (ActionablePath(node ?? Selection ?? CurrentFolder) is { } path) services.Shell.ShowInExplorer(path);
    }

    public void ShowProperties(NodeRef? node)
    {
        if (ActionablePath(node ?? Selection ?? CurrentFolder) is { } path) services.Shell.ShowProperties(path);
    }

    // MARK: Derived collections

    private void RefreshLayout()
    {
        if (CurrentFolder is not { } folder)
        {
            Segments = [];
            chains = new Dictionary<string, IReadOnlyList<NodeRef>>(StringComparer.Ordinal);
            slots = new Dictionary<string, int>();
            return;
        }
        Segments = SunburstLayout.Segments(folder.Node, folder.Path);
        slots = SunburstLayout.SlotAssignments(folder.Node, folder.Path);
        var map = new Dictionary<string, IReadOnlyList<NodeRef>>(StringComparer.Ordinal);
        Walk(folder, [folder]);
        chains = map;

        void Walk(NodeRef node, List<NodeRef> chain)
        {
            if (chain.Count > SunburstLayout.RingCount) return;
            foreach (var child in node.Children)
            {
                var childChain = new List<NodeRef>(chain) { child };
                map[child.Id] = childChain;
                if (child.Node.Kind == NodeKind.Directory) Walk(child, childChain);
            }
        }
    }

    private void RefreshRows()
    {
        if (CurrentFolder is not { } folder)
        {
            Rows = [];
            return;
        }
        string query = SearchText.Trim();
        if (query.Length == 0)
        {
            Rows = Sorted(folder.Children.Where(child => child.Node.AllocatedSize > 0 || child.Node.Kind == NodeKind.Inaccessible));
            return;
        }
        var matches = new List<NodeRef>();
        Search(folder, [folder]);
        Rows = Sorted(matches);

        void Search(NodeRef node, List<NodeRef> chain)
        {
            foreach (var child in node.Children)
            {
                if (matches.Count >= SearchResultLimit) return;
                var childChain = new List<NodeRef>(chain) { child };
                if (child.Node.Name.Length > 0 && child.Node.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                {
                    chains.TryAdd(child.Id, childChain);
                    matches.Add(child);
                }
                if (child.Node.Kind == NodeKind.Directory) Search(child, childChain);
            }
        }
    }

    private List<NodeRef> Sorted(IEnumerable<NodeRef> rows)
    {
        var ordered = SortColumn == SortColumn.Name
            ? rows.OrderBy(row => Title(row), StringComparer.CurrentCultureIgnoreCase)
            : rows.OrderBy(row => row.Node.AllocatedSize);
        var list = ordered.ToList();
        if (SortDescending) list.Reverse();
        return list;
    }

    /// <summary>Stops a running scan; the model is no longer used after this.</summary>
    public void Dispose()
    {
        scanGeneration++;
        scanCancellation?.Cancel();
        scanCancellation?.Dispose();
        scanCancellation = null;
    }

    /// <summary>Tells views everything may have changed: tree, selection and derived collections move together.</summary>
    private void Changed() => OnPropertyChanged(string.Empty);
}
