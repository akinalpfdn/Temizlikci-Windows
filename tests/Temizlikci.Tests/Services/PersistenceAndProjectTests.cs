using Temizlikci.Domain.Actions;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Tree;
using Temizlikci.Services.Persistence;
using Temizlikci.Services.Projects;
using Temizlikci.Services.RecycleBin;
using Temizlikci.Tests.Support;
using static Temizlikci.Tests.Support.TreeBuilder;

namespace Temizlikci.Tests.Services;

public sealed class FileScanCacheTests
{
    [Fact]
    public void Should_SaveAndLoadALocationsScan_When_ReopeningIt()
    {
        using var tree = new FixtureTree();
        var cache = new FileScanCache(tree.Path("Scans"));
        var scannedAt = new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);

        cache.Save(Sample(), scannedAt, RootPath);
        var loaded = cache.Load(RootPath);

        Assert.NotNull(loaded);
        Assert.Equal(scannedAt, loaded.ScannedAtUtc);
        Assert.Equal(1_000, loaded.Root.AllocatedSize);
        Assert.NotNull(loaded.Root.Locate(@"C:\Scan\Docs\Reports\q1.pdf"));
    }

    [Fact]
    public void Should_FindNothing_When_TheLocationWasNeverSavedOrWasRemoved()
    {
        using var tree = new FixtureTree();
        var cache = new FileScanCache(tree.Path("Scans"));
        cache.Save(Sample(), DateTime.UtcNow, RootPath);

        Assert.Null(cache.Load(@"D:\Other"));
        cache.Remove(RootPath);
        Assert.Null(cache.Load(RootPath));
    }

    [Fact]
    public void Should_UseTheSameEntry_When_ThePathDiffersOnlyInCase()
    {
        using var tree = new FixtureTree();
        var cache = new FileScanCache(tree.Path("Scans"));
        cache.Save(Sample(), DateTime.UtcNow, RootPath);

        Assert.NotNull(cache.Load(RootPath.ToUpperInvariant()));
    }
}

public sealed class FileSnapshotStoreTests
{
    [Fact]
    public void Should_SaveLoadTheNewestAndPruneOldSnapshots_When_KeepingHistoryPerLocation()
    {
        using var tree = new FixtureTree();
        var store = new FileSnapshotStore(tree.Path("Snapshots"));
        for (int day = 1; day <= 4; day++)
        {
            store.Save(new ScanSnapshot(1, @"C:\R", new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc),
                new Dictionary<string, long> { [@"C:\R"] = day }, new HashSet<string> { @"C:\R\locked" }));
        }
        store.Save(new ScanSnapshot(1, @"D:\Other", DateTime.UtcNow, new Dictionary<string, long>(), new HashSet<string>()));

        Assert.Equal(4, store.Latest(@"C:\R")!.Sizes[@"C:\R"]);
        Assert.Contains(@"c:\r\LOCKED", store.Latest(@"C:\R")!.UnreadPaths);

        store.Prune(@"C:\R", 2);

        Assert.Equal(2, store.Recent(@"C:\R", 10).Count);
        Assert.Equal(4, store.Latest(@"C:\R")!.Sizes[@"C:\R"]);
        Assert.Single(store.Recent(@"D:\Other", 10));
        Assert.Null(store.Latest(@"E:\Missing"));
    }
}

public sealed class FileSystemProjectTests
{
    [Fact]
    public void Should_FindMarkersByNameAndByPattern_When_LookingInAFolder()
    {
        using var tree = new FixtureTree();
        tree.File(@"api\Api.csproj", 10);
        tree.File(@"api\package.json", 10);
        var checker = new FileSystemMarkerChecker();

        Assert.True(checker.Contains(tree.Path("api"), "package.json"));
        Assert.Equal(tree.Path(@"api\Api.csproj"), checker.FirstMatch(tree.Path("api"), "*.csproj"));
        Assert.False(checker.Contains(tree.Path("api"), "*.sln"));
        Assert.False(checker.Contains(tree.Path("missing"), "*.csproj"));
    }

    [Fact]
    public void Should_ReadActivityFromTheGitIndexAndOtherwiseFromTheNewestFile_When_AskedWhenAProjectWasTouched()
    {
        using var tree = new FixtureTree();
        var old = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var recent = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        tree.File(@"Proj\src\Program.cs", 10, old);
        tree.File(@"Proj\bin\out.dll", 10, recent);
        var reader = new FileSystemProjectActivity();

        Assert.Equal(old, reader.LastTouchedUtc(tree.Path("Proj"), new HashSet<string> { "bin" }));

        tree.File(@"Proj\.git\index", 10, recent);
        Assert.Equal(recent, reader.LastTouchedUtc(tree.Path("Proj"), new HashSet<string> { "bin" }));
    }
}

public sealed class ShellRecycleBinTests
{
    /// <summary>Put Back is tested on a fake Recycle Bin entry in a temporary folder — never the real bin.</summary>
    [Fact]
    public void Should_MoveTheEntryBackAndRemoveItsRecord_When_PuttingBack()
    {
        using var tree = new FixtureTree();
        tree.File(@"bin\$RABC123.txt", 100);
        tree.File(@"bin\$IABC123.txt", 20);
        var bin = new ShellRecycleBin(() => 0);

        bin.PutBack(new RecycledItem(tree.Path(@"docs\notes.txt"), tree.Path(@"bin\$RABC123.txt")));

        Assert.True(System.IO.File.Exists(tree.Path(@"docs\notes.txt")));
        Assert.False(System.IO.File.Exists(tree.Path(@"bin\$RABC123.txt")));
        Assert.False(System.IO.File.Exists(tree.Path(@"bin\$IABC123.txt")));
    }

    [Fact]
    public void Should_RefuseToOverwrite_When_SomethingTookTheItemsPlace()
    {
        using var tree = new FixtureTree();
        tree.File(@"bin\$RXYZ.txt", 100);
        tree.File(@"docs\notes.txt", 5);
        var bin = new ShellRecycleBin(() => 0);

        var error = Assert.Throws<RecycleException>(() => bin.PutBack(new RecycledItem(tree.Path(@"docs\notes.txt"), tree.Path(@"bin\$RXYZ.txt"))));

        Assert.Equal(RecycleFailure.Occupied, error.Failure);
        Assert.True(System.IO.File.Exists(tree.Path(@"bin\$RXYZ.txt")));
    }

    [Fact]
    public void Should_ReportWhetherEntriesAreStillThere_When_Reconciling()
    {
        using var tree = new FixtureTree();
        tree.File(@"bin\$RONE.txt", 10);
        var bin = new ShellRecycleBin(() => 0);

        var presence = bin.Presence([new RecycledItem("a", tree.Path(@"bin\$RONE.txt")), new RecycledItem("b", tree.Path(@"bin\$RTWO.txt")), new RecycledItem("c", string.Empty)]);

        Assert.Equal([RecyclePresence.Present, RecyclePresence.Gone, RecyclePresence.Gone], presence);
    }

    [Fact]
    public void Should_ReportAMissingItem_When_ItVanishedSinceTheScan()
    {
        using var tree = new FixtureTree();
        var bin = new ShellRecycleBin(() => 0);

        var error = Assert.Throws<RecycleException>(() => bin.MoveToRecycleBin(tree.Path("gone.txt")));

        Assert.Equal(RecycleFailure.Missing, error.Failure);
    }

    [Theory]
    [InlineData(@"C:\$Recycle.Bin\S-1\$R5Q2X.zip", @"C:\$Recycle.Bin\S-1\$I5Q2X.zip")]
    [InlineData(@"C:\other\file.zip", "")]
    public void Should_NameTheRecordFile_When_GivenARecycledEntry(string recycled, string expected)
    {
        Assert.Equal(expected, ShellRecycleBin.InformationFile(recycled));
    }
}
