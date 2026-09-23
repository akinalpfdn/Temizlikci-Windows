using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;
using Temizlikci.Services.Scanning;
using Temizlikci.Tests.Support;

namespace Temizlikci.Tests.Services;

public sealed class DirectoryScannerTests
{
    /// <summary>A low threshold, so "large" files stay small enough to write quickly.</summary>
    private static readonly ScanConfiguration Configuration = new() { IndividualFileThreshold = 100 * 1024, ProgressInterval = TimeSpan.FromMilliseconds(10) };

    private static async Task<(ScanResult Result, List<ScanEvent> Events)> Scan(string root, ScanConfiguration? configuration = null)
    {
        var events = new List<ScanEvent>();
        await foreach (var scanEvent in new DirectoryScanner().ScanAsync(root, configuration ?? Configuration, TestContext.Current.CancellationToken))
        {
            events.Add(scanEvent);
        }
        var finished = Assert.IsType<ScanEvent.Finished>(events[^1]);
        return (finished.Result, events);
    }

    [Fact]
    public async Task Should_MeasureAllocatedSizesExactly_When_ScanningNestedFolders()
    {
        using var tree = new FixtureTree();
        string small = tree.File(@"a\x.bin", 10_000);
        string nested = tree.File(@"a\b\y.bin", 50_000);
        string large = tree.File("c.bin", 200_000);
        tree.Folder("empty");

        var (result, _) = await Scan(tree.Root);

        long expected = FixtureTree.AllocatedSize(small) + FixtureTree.AllocatedSize(nested) + FixtureTree.AllocatedSize(large);
        Assert.Equal(expected, result.Root.AllocatedSize);
        Assert.Equal(3, result.FileCount);
        Assert.Equal(tree.Root, result.Root.Name);
        Assert.Equal(FixtureTree.AllocatedSize(large), result.Root.Children.Single(child => child.Name == "c.bin").AllocatedSize);
        var a = result.Root.Children.Single(child => child.Name == "a");
        Assert.Contains(a.Children, child => child.Kind == NodeKind.SmallerFiles && child.FileCount == 1);
        Assert.Contains(result.Root.Children, child => child.Name == "empty" && child.AllocatedSize == 0);
        Assert.Equal(ScanMethod.DirectoryWalk, result.Method);
    }

    [Fact]
    public async Task Should_CountAHardLinkedFileOnce_When_BothNamesAreScanned()
    {
        using var tree = new FixtureTree();
        string original = tree.File(@"one\data.bin", 300_000);
        tree.HardLink(@"one\data.bin", @"two\same.bin");

        var (result, _) = await Scan(tree.Root);

        Assert.Equal(FixtureTree.AllocatedSize(original), result.Root.AllocatedSize);
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public async Task Should_NeitherLoopNorDoubleCount_When_AJunctionPointsAtAnAncestor()
    {
        using var tree = new FixtureTree();
        string file = tree.File(@"work\data.bin", 150_000);
        tree.Junction(@"work\loop", tree.Root);
        tree.Junction("elsewhere", Path.GetTempPath());

        var (result, _) = await Scan(tree.Root);

        Assert.Equal(FixtureTree.AllocatedSize(file), result.Root.AllocatedSize);
        var work = result.Root.Children.Single(child => child.Name == "work");
        Assert.DoesNotContain(work.Children, child => child.Name == "loop");
        Assert.DoesNotContain(result.Root.Children, child => child.Name == "elsewhere");
    }

    [Fact]
    public async Task Should_KeepScanning_When_AFolderCannotBeRead()
    {
        using var tree = new FixtureTree();
        tree.File(@"locked\secret.bin", 20_000);
        tree.File("open.bin", 20_000);
        tree.DenyListing("locked");

        var (result, _) = await Scan(tree.Root);

        var locked = result.Root.Children.Single(child => child.Name == "locked");
        Assert.Equal(NodeKind.Inaccessible, locked.Kind);
        Assert.Equal(1, result.InaccessibleCount);
        Assert.Equal(1, result.FileCount);
    }

    [Fact]
    public async Task Should_ReportAMissingRoot_When_TheFolderDoesNotExist()
    {
        using var tree = new FixtureTree();

        var error = await Assert.ThrowsAsync<ScanException>(() => Scan(tree.Path("nope")));

        Assert.Equal(ScanFailure.RootNotFound, error.Failure);
    }

    [Fact]
    public async Task Should_RefuseAFile_When_ItIsGivenAsTheRoot()
    {
        using var tree = new FixtureTree();
        string file = tree.File("file.txt", 10);

        var error = await Assert.ThrowsAsync<ScanException>(() => Scan(file));

        Assert.Equal(ScanFailure.RootNotFolder, error.Failure);
    }

    [Fact]
    public async Task Should_StopPromptly_When_TheScanIsCancelled()
    {
        using var tree = new FixtureTree();
        for (int index = 0; index < 20; index++) tree.File($@"d{index}\f.bin", 1_000);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in new DirectoryScanner().ScanAsync(tree.Root, Configuration, cancellation.Token))
            {
            }
        });
    }

    [Fact]
    public async Task Should_StopTheWalk_When_TheConsumerStopsReadingEarly()
    {
        using var tree = new FixtureTree();
        for (int index = 0; index < 50; index++) tree.File($@"d{index % 5}\sub{index}\f.bin", 1_000);

        await foreach (var _ in new DirectoryScanner().ScanAsync(tree.Root, Configuration, TestContext.Current.CancellationToken))
        {
            break;
        }

        // Leaving the loop disposes the stream, which cancels the walk and waits for it; reaching here without a hang
        // or an unobserved exception is the assertion.
        Assert.True(Directory.Exists(tree.Root));
    }

    [Fact]
    public async Task Should_EndWithTheResult_When_ProgressWasPublishedBefore()
    {
        using var tree = new FixtureTree();
        for (int index = 0; index < 200; index++) tree.File($@"d{index % 10}\s{index % 7}\f{index}.bin", 4_096);

        var (result, events) = await Scan(tree.Root);

        Assert.Single(events.OfType<ScanEvent.Finished>());
        Assert.All(events[..^1], scanEvent => Assert.IsType<ScanEvent.Progress>(scanEvent));
        Assert.Equal(200, result.FileCount);
    }

    [Fact]
    public async Task Should_NotVisitSkippedPaths_When_TheConfigurationListsThem()
    {
        using var tree = new FixtureTree();
        tree.File(@"keep\a.bin", 10_000);
        tree.File(@"skip\b.bin", 10_000);
        var configuration = Configuration with { SkippedPaths = new HashSet<string>(NodePath.Comparer) { tree.Path("SKIP") } };

        var (result, _) = await Scan(tree.Root, configuration);

        Assert.DoesNotContain(result.Root.Children, child => child.Name == "skip");
        Assert.Equal(1, result.FileCount);
    }

    [Fact]
    public async Task Should_ReadFoldersBeyondMaxPath_When_PathsAreLong()
    {
        using var tree = new FixtureTree();
        string deep = string.Join('\\', Enumerable.Repeat(new string('d', 60), 6));
        string file = tree.File(Path.Combine(deep, "deep.bin"), 150_000);
        Assert.True(file.Length > 260);

        var (result, _) = await Scan(tree.Root);

        Assert.Equal(1, result.FileCount);
        Assert.Equal(FixtureTree.AllocatedSize(file), result.Root.AllocatedSize);
    }

    [Fact]
    public async Task Should_KeepModificationDates_When_ListingLargeFiles()
    {
        using var tree = new FixtureTree();
        var date = new DateTime(2024, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        tree.File("dated.bin", 200_000, date);

        var (result, _) = await Scan(tree.Root);

        Assert.Equal(date, result.Root.Children.Single(child => child.Name == "dated.bin").ModifiedUtc);
    }
}
