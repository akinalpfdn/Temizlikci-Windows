using Temizlikci.Domain.History;
using Temizlikci.Domain.Tree;
using static Temizlikci.Tests.Support.TreeBuilder;

namespace Temizlikci.Tests.Domain;

public sealed class ScanArchiveTests
{
    private static readonly DateTime ScannedAt = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

    private static FileNode SampleTree() => FileNode.Directory(RootPath, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    [
        Dir("Apps", File("Big.app", 500, new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc)), FileNode.SmallerFiles(42, 99)),
        FileNode.Inaccessible("Private"),
        FileNode.Unattributed(1_000),
        Dir("Ünïcödé folder", File("日本語.txt", 20_000_000)),
    ]);

    private static void AssertSame(FileNode expected, FileNode actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.AllocatedSize, actual.AllocatedSize);
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.ModifiedUtc, actual.ModifiedUtc);
        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (int index = 0; index < expected.Children.Count; index++) AssertSame(expected.Children[index], actual.Children[index]);
    }

    [Fact]
    public void Should_RestoreTheWholeTree_When_DecodingWhatWasEncoded()
    {
        var tree = SampleTree();

        var archived = ScanArchive.Decode(ScanArchive.Encode(tree, ScannedAt, RootPath));

        Assert.Equal(RootPath, archived.LocationPath);
        Assert.Equal(ScannedAt, archived.ScannedAtUtc);
        AssertSame(tree, archived.Root);
        Assert.NotNull(archived.Root.Locate(@"C:\Scan\Apps\Big.app"));
    }

    [Fact]
    public void Should_RefuseData_When_ItIsNotAnArchive()
    {
        var error = Assert.Throws<ScanArchive.ArchiveException>(() => ScanArchive.Decode("not an archive"u8));
        Assert.Equal(ScanArchive.Failure.NotAnArchive, error.Reason);
    }

    [Fact]
    public void Should_RefuseData_When_ItComesFromAnotherVersion()
    {
        var data = ScanArchive.Encode(SampleTree(), ScannedAt, RootPath);
        data[4] = 99;

        var error = Assert.Throws<ScanArchive.ArchiveException>(() => ScanArchive.Decode(data));
        Assert.Equal(ScanArchive.Failure.UnsupportedVersion, error.Reason);
        Assert.Equal(99u, error.FoundVersion);
    }

    [Fact]
    public void Should_RefuseData_When_ItIsCutShort()
    {
        var data = ScanArchive.Encode(SampleTree(), ScannedAt, RootPath).AsSpan(0, 30).ToArray();

        var error = Assert.Throws<ScanArchive.ArchiveException>(() => ScanArchive.Decode(data));
        Assert.Equal(ScanArchive.Failure.Truncated, error.Reason);
    }

    [Theory]
    [InlineData(RefreshPeriod.Day, 1)]
    [InlineData(RefreshPeriod.Week, 7)]
    public void Should_ConvertToDays_When_AskingForARefreshInterval(RefreshPeriod period, int days)
    {
        Assert.Equal(TimeSpan.FromDays(days), period.Interval());
    }

    [Fact]
    public void Should_NeverRefresh_When_ThePeriodIsNever()
    {
        Assert.Null(RefreshPeriod.Never.Interval());
    }
}

public sealed class SnapshotBuilderTests
{
    private const long Mb = 1_000_000;

    [Fact]
    public void Should_RecordTopLevelsLargeItemsRequestedPathsAndUnreadFolders_When_TakingASnapshot()
    {
        var tree = FileNode.Directory(@"C:\R", null,
        [
            Dir("a", Dir("b", Dir("small", File("x", 5 * Mb)), Dir("big", File("y", 300 * Mb)))),
            FileNode.Inaccessible("locked"),
        ]);

        var snapshot = SnapshotBuilder.Snapshot(tree, @"C:\R", DateTime.UtcNow, new HashSet<string> { @"C:\R\a\b\small\x" });

        Assert.True(snapshot.Sizes.ContainsKey(@"C:\R"));
        Assert.True(snapshot.Sizes.ContainsKey(@"C:\R\a\b"));
        Assert.Equal(300 * Mb, snapshot.Sizes[@"C:\R\a\b\big"]);
        Assert.Equal(5 * Mb, snapshot.Sizes[@"C:\R\a\b\small\x"]);
        Assert.False(snapshot.Sizes.ContainsKey(@"C:\R\a\b\small"));
        Assert.Equal([@"C:\R\locked"], snapshot.UnreadPaths);
    }
}

public sealed class GrowthReportTests
{
    private const long Mb = 1_000_000;

    private static ScanSnapshot Snapshot(Dictionary<string, long> sizes, int day, params string[] unread) =>
        new(1, @"C:\R", new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc), sizes, new HashSet<string>(unread));

    [Fact]
    public void Should_ClassifyGrowthShrinkingNewAndRemovedItems_When_AboveTheNoiseFloor()
    {
        var before = Snapshot(new() { [@"C:\R\a"] = 100 * Mb, [@"C:\R\b"] = 500 * Mb, [@"C:\R\gone"] = 50 * Mb, [@"C:\R\steady"] = 40 * Mb }, 1);
        var after = Snapshot(new() { [@"C:\R\a"] = 400 * Mb, [@"C:\R\b"] = 200 * Mb, [@"C:\R\new"] = 80 * Mb, [@"C:\R\steady"] = 41 * Mb }, 2);

        var report = GrowthReport.Compare(before, after);

        Assert.Equal(GrowthKind.Grew, report.ChangeFor(@"C:\R\a")?.Kind);
        Assert.Equal(300 * Mb, report.ChangeFor(@"C:\R\a")?.Delta);
        Assert.Equal(GrowthKind.Shrank, report.ChangeFor(@"C:\R\b")?.Kind);
        Assert.Equal(GrowthKind.Appeared, report.ChangeFor(@"C:\R\new")?.Kind);
        Assert.Equal(GrowthKind.Removed, report.ChangeFor(@"C:\R\gone")?.Kind);
        Assert.Null(report.ChangeFor(@"C:\R\steady"));
    }

    [Fact]
    public void Should_IgnoreFolders_When_TheyWereUnreadInEitherScan()
    {
        var before = Snapshot(new() { [@"C:\R\Documents\x"] = 900 * Mb }, 1);
        var after = Snapshot([], 2, @"C:\R\Documents");

        Assert.Empty(GrowthReport.Compare(before, after).Changes);
    }

    [Fact]
    public void Should_ListWhereGrowthHappened_When_AParentIsMostlyExplainedByOneChild()
    {
        var before = Snapshot(new() { [@"C:\R\AppData"] = 1_000 * Mb, [@"C:\R\AppData\npm-cache"] = 100 * Mb, [@"C:\R\Docs"] = 100 * Mb }, 1);
        var after = Snapshot(new() { [@"C:\R\AppData"] = 1_950 * Mb, [@"C:\R\AppData\npm-cache"] = 1_000 * Mb, [@"C:\R\Docs"] = 300 * Mb }, 2);

        var biggest = GrowthReport.Compare(before, after).BiggestChanges();

        Assert.Equal([@"C:\R\AppData\npm-cache", @"C:\R\Docs"], biggest.Select(change => change.Path));
    }
}

public sealed class LargeFileFinderTests
{
    [Fact]
    public void Should_ListEveryFileLargestFirst_When_FindingLargeFiles()
    {
        var root = Sample();

        var files = LargeFileFinder.Largest(root);

        Assert.Equal([500L, 200, 100, 100, 100], files.Select(file => file.Node.AllocatedSize));
        Assert.Equal(new HashSet<string> { "Big.app", "Small.app", "q1.pdf", "notes.txt", "movie.mov" }, files.Select(f => f.Node.Name).ToHashSet());
        var q1 = files.Single(file => file.Node.Name == "q1.pdf");
        Assert.Equal(@"C:\Scan\Docs\Reports\q1.pdf", q1.Path);
        Assert.Equal(["Docs", "Reports", "q1.pdf"], root.NodesAlong(q1.IdPath).Skip(1).Select(item => item.Node.Name));
    }

    [Fact]
    public void Should_SkipSmallerFilesAndStopAtTheLimit_When_FindingLargeFiles()
    {
        var root = Root(File("a", 300), File("b", 200), File("c", 100), FileNode.SmallerFiles(40, 900));

        var files = LargeFileFinder.Largest(root, limit: 2);

        Assert.Equal(["a", "b"], files.Select(file => file.Node.Name));
    }
}
