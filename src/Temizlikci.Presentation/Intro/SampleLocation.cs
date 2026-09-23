using System.Runtime.CompilerServices;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;
using Temizlikci.Presentation.Actions;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.Presentation.Intro;

/// <summary>
/// The example folder in the chart introduction: a real, clickable sunburst on made-up data. Its services never touch
/// the disk: the scan replays a fixed tree, nothing is cached or remembered, and the Recycle Bin refuses everything.
/// </summary>
public static class SampleLocation
{
    public const string RootPath = @"C:\Example";

    private const long Gigabyte = 1_000_000_000;

    public static ScanLocation Location => new(RootPath, L10n.IntroSampleName, IsWholeVolume: false);

    /// <summary>The example's folder names, looked up as <c>intro.sample.{key}</c>.</summary>
    public static IReadOnlyList<string> FolderKeys { get; } = ["photos", "vacation", "family", "projects", "game", "website", "music", "apps", "caches"];

    /// <summary>Services for the example: the real read-only ones where harmless, inert ones everywhere else.</summary>
    public static LocationServices Services(LocationServices real, KnownLocations locations)
    {
        ArgumentNullException.ThrowIfNull(real);
        var bin = new RefusingRecycleBin();
        var markers = new NoMarkers();
        return new LocationServices
        {
            Scanner = new SampleScanner(),
            Volumes = real.Volumes,
            Shell = new NoShell(),
            RecycleBin = bin,
            Ledger = new RecycleLedger(bin),
            Undo = new UndoHistory(),
            Rules = new RuleEngine([], locations, markers),
            Projects = new ProjectFinder(markers, new NoActivity(), locations),
            Protection = real.Protection,
            Identifier = real.Identifier,
            Cache = new NoCache(),
            Snapshots = new NoSnapshots(),
            HiddenSpace = real.HiddenSpace,
        };
    }

    public static FileNode Tree()
    {
        static string Name(string key) => L10n.Get("intro.sample." + key);
        static FileNode Folder(string key, params FileNode[] children) => FileNode.Directory(Name(key), null, children);
        static FileNode File(string name, long gigabytes) => FileNode.File(name, gigabytes * Gigabyte, null);

        return FileNode.Directory(RootPath, null,
        [
            Folder("photos", Folder("vacation", File("IMG_0001.mov", 9), File("IMG_0002.mov", 5)), Folder("family", File("IMG_0100.heic", 6))),
            Folder("projects", Folder("game", File("Assets.pak", 8)), Folder("website", File("video.mp4", 3))),
            Folder("music", File("Library.musiclibrary", 7)),
            Folder("apps", File("Editor.exe", 4)),
            Folder("caches", File("cache.db", 2)),
        ]);
    }

    private sealed class SampleScanner : IDiskScanner
    {
        public async IAsyncEnumerable<ScanEvent> ScanAsync(string root, ScanConfiguration configuration, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            var tree = Tree();
            yield return new ScanEvent.Finished(new ScanResult(tree, TimeSpan.Zero, tree.FileCount, DirectoryCount: 10, InaccessibleCount: 0, ScanMethod.DirectoryWalk));
        }
    }

    /// <summary>The example must never move anything.</summary>
    private sealed class RefusingRecycleBin : IRecycleBin
    {
        public RecycledItem MoveToRecycleBin(string path) => throw new RecycleException(RecycleFailure.Protected, NodePath.Name(path));

        public void PutBack(RecycledItem item) => throw new RecycleException(RecycleFailure.PutBackFailed, NodePath.Name(item?.OriginalPath ?? RootPath));

        public IReadOnlyList<RecyclePresence> Presence(IReadOnlyList<RecycledItem> items) => items.Select(_ => RecyclePresence.Unknown).ToList();

        public void Open()
        {
            // The example has no Recycle Bin of its own to show.
        }
    }

    private sealed class NoShell : IShell
    {
        public void ShowInExplorer(string path)
        {
            // Nothing in the example exists on disk.
        }

        public void ShowProperties(string path)
        {
            // Nothing in the example exists on disk.
        }

        public void OpenUri(Uri uri)
        {
            // The example opens nothing.
        }

        public void OpenSystemProtection()
        {
            // The example opens nothing.
        }
    }

    private sealed class NoMarkers : IMarkerChecker
    {
        public bool Contains(string folder, string marker) => false;

        public string? FirstMatch(string folder, string marker) => null;
    }

    private sealed class NoActivity : IProjectActivityReader
    {
        public DateTime? LastTouchedUtc(string projectPath, IReadOnlySet<string> ignoringNames) => null;
    }

    /// <summary>The example keeps no saved scan.</summary>
    private sealed class NoCache : IScanCache
    {
        public void Save(FileNode root, DateTime scannedAtUtc, string locationPath)
        {
            // Nothing to keep: the example is rebuilt each time.
        }

        public ScanArchive.Archived? Load(string locationPath) => null;

        public void Remove(string locationPath)
        {
            // Nothing was saved.
        }
    }

    /// <summary>The example keeps no history.</summary>
    private sealed class NoSnapshots : ISnapshotStore
    {
        public IReadOnlyList<ScanSnapshot> Recent(string locationPath, int limit) => [];

        public void Save(ScanSnapshot snapshot)
        {
            // Nothing to remember.
        }

        public void Prune(string locationPath, int count)
        {
            // Nothing was saved.
        }
    }
}
