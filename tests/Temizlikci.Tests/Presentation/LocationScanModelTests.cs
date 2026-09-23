using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Layout;
using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;
using Temizlikci.Presentation.Overview;
using Temizlikci.Tests.Support;
using static Temizlikci.Tests.Support.TreeBuilder;

namespace Temizlikci.Tests.Presentation;

public sealed class LocationScanModelTests
{
    private readonly ModelFixture fixture = new();

    private static NodeRef Child(string name, NodeRef? folder) =>
        folder!.Value.Children.Single(child => child.Node.Name == name);

    private static ScanEvent.Finished Finished(FileNode root) =>
        new ScanEvent.Finished(new ScanResult(root, TimeSpan.FromSeconds(1), 0, 0, 0, ScanMethod.DirectoryWalk));

    [Fact]
    public async Task Should_ShowTheResultWithTheLocationsName_When_TheScanFinishes()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        Assert.Equal(ScanPhase.Finished, model.Phase);
        Assert.Equal(1_000, model.Tree!.AllocatedSize);
        Assert.Equal("Scan Place", model.Title(model.CurrentFolder!.Value));
        Assert.Equal(["Apps", "Docs", "movie.mov"], model.Rows.Select(row => row.Node.Name));
        Assert.NotEmpty(model.Segments);
    }

    [Fact]
    public async Task Should_AddTheVolumesUnattributedSpace_When_TheWholeVolumeWasScanned()
    {
        var model = await ModelFixture.Scanned(fixture.Make(wholeVolume: true));

        Assert.Equal(1_200, model.Tree!.AllocatedSize);
        Assert.Contains(model.Tree.Children, child => child.Kind == NodeKind.Unattributed && child.AllocatedSize == 200);
    }

    [Fact]
    public async Task Should_ShowMeasuredFoldersAndTheUnmeasuredRemainder_When_Scanning()
    {
        var progress = new ScanProgress { AllocatedSize = 600, CompletedTopLevel = [Dir("Apps", File("Big.app", 600))] };
        var model = await ModelFixture.Scanned(fixture.Make([new ScanEvent.Progress(progress)], wholeVolume: true));

        Assert.Equal(ScanPhase.Scanning, model.Phase);
        Assert.Equal(1_200, model.Tree!.AllocatedSize);
        Assert.Contains(model.Tree.Children, child => child.Kind == NodeKind.Pending && child.AllocatedSize == 600);
        Assert.False(model.CanGoBack);
    }

    [Fact]
    public async Task Should_ShowFoldersStillBeingReadWithTheirSizeSoFar_When_ProgressArrives()
    {
        var progress = new ScanProgress
        {
            CompletedTopLevel = [Dir("Apps", File("Big.app", 300))],
            MeasuringTopLevel = new Dictionary<string, long> { ["Docs"] = 500 },
        };
        var model = await ModelFixture.Scanned(fixture.Make([new ScanEvent.Progress(progress)], wholeVolume: true));

        var growing = model.Tree!.Children.Single(child => child.Name == "Docs");
        Assert.Equal(500, growing.AllocatedSize);
        Assert.Empty(growing.Children);
        Assert.Single(model.MeasuringIds);
        Assert.Contains(model.Tree.Children, child => child.Kind == NodeKind.Pending && child.AllocatedSize == 400);
    }

    [Fact]
    public async Task Should_IgnoreAProgressUpdate_When_ItArrivesAfterTheScanFinished()
    {
        var late = new ScanProgress { CompletedTopLevel = Sample().Children };
        var model = await ModelFixture.Scanned(fixture.Make([Finished(Sample()), new ScanEvent.Progress(late)], wholeVolume: true));

        Assert.Equal(ScanPhase.Finished, model.Phase);
        Assert.DoesNotContain(model.Tree!.Children, child => child.Kind == NodeKind.Pending);
        Assert.Contains(model.Tree.Children, child => child.Kind == NodeKind.Unattributed);
    }

    [Fact]
    public async Task Should_ExplainTheFailure_When_TheRootIsMissing()
    {
        var model = await ModelFixture.Scanned(fixture.Make([], new ScanException(ScanFailure.RootNotFound, RootPath)));

        Assert.Equal(ScanPhase.Failed, model.Phase);
        Assert.Contains("Scan", model.FailureMessage, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(model.FailureSuggestion));
        Assert.Null(model.Tree);
    }

    [Fact]
    public async Task Should_ClearResults_When_TheScanIsStopped()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        model.StopScan();

        Assert.Equal(ScanPhase.Idle, model.Phase);
        Assert.Null(model.Tree);
        Assert.Empty(model.Rows);
    }

    [Fact]
    public async Task Should_OpenFoldersGoUpAndMoveThroughHistory_When_Navigating()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        model.Open(Child("Docs", model.CurrentFolder));
        Assert.Equal("Docs", model.CurrentFolder!.Value.Node.Name);
        Assert.True(model.CanGoBack && model.CanGoUp && !model.CanGoForward);

        model.GoBack();
        Assert.Equal(RootPath, model.CurrentFolder!.Value.Id);
        Assert.True(model.CanGoForward);

        model.GoForward();
        Assert.Equal("Docs", model.CurrentFolder!.Value.Node.Name);

        model.GoUp();
        Assert.Equal(RootPath, model.CurrentFolder!.Value.Id);
        Assert.Equal("Docs", model.Selection!.Value.Node.Name);
        Assert.False(model.CanGoForward);
    }

    [Fact]
    public async Task Should_OpenAFolderTwoRingsDeepInOneStep_When_KeepingThePath()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        model.Open(model.NodeById(Id(@"Docs\Reports"))!.Value);
        Assert.Equal([RootPath, "Docs", "Reports"], model.Path.Select(item => item.Node.Name));

        model.GoToAncestor(0);
        Assert.Single(model.Path);
    }

    [Fact]
    public async Task Should_NotOpenFiles_When_AskedTo()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        model.Open(Child("movie.mov", model.CurrentFolder));

        Assert.Single(model.Path);
    }

    [Fact]
    public async Task Should_MoveTheSelectionBetweenSiblingsAndRings_When_UsingTheKeyboard()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        model.SelectSibling(1);
        Assert.Equal("Apps", model.Selection!.Value.Node.Name);
        model.SelectSibling(-1);
        Assert.Equal("movie.mov", model.Selection!.Value.Node.Name);
        model.SelectSibling(-1);
        Assert.Equal("Docs", model.Selection!.Value.Node.Name);

        model.SelectChildRing();
        Assert.Equal("Reports", model.Selection!.Value.Node.Name);
        model.SelectParentRing();
        Assert.Equal("Docs", model.Selection!.Value.Node.Name);

        model.OpenSelection();
        Assert.Equal("Docs", model.CurrentFolder!.Value.Node.Name);
    }

    [Fact]
    public async Task Should_SearchBelowTheOpenFolderAndOpenAResultsFolder_When_Typing()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        model.SearchText = "q1";
        Assert.Equal(["q1.pdf"], model.Rows.Select(row => row.Node.Name));

        model.SearchText = "zzz";
        Assert.Empty(model.Rows);

        model.SearchText = "REPORTS";
        model.Open(model.Rows[0]);
        Assert.Equal([RootPath, "Docs", "Reports"], model.Path.Select(item => item.Node.Name));
        Assert.Empty(model.SearchText);
    }

    [Fact]
    public async Task Should_SortRowsByTheChosenColumn_When_TheSortChanges()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        model.SortColumn = SortColumn.Name;
        model.SortDescending = false;

        Assert.Equal(["Apps", "Docs", "movie.mov"], model.Rows.Select(row => row.Node.Name));
        model.SortDescending = true;
        Assert.Equal(["movie.mov", "Docs", "Apps"], model.Rows.Select(row => row.Node.Name));
    }

    [Fact]
    public async Task Should_ShowTheSelectionInExplorer_When_OneIsSelectedOrElseTheOpenFolder()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        model.ShowInExplorer(null);
        var apps = Child("Apps", model.CurrentFolder);
        model.Select(apps);
        model.ShowInExplorer(null);

        Assert.Equal([RootPath, apps.Path], fixture.Shell.Shown);
    }

    [Fact]
    public async Task Should_MatchEachRowsColorToItsSegment_When_Drawing()
    {
        var model = await ModelFixture.Scanned(fixture.Make());
        var apps = Child("Apps", model.CurrentFolder);

        Assert.Equal(SegmentFill.ForSlot(0, 1), model.Fill(apps));
        Assert.Equal(model.Segments.Single(segment => segment.NodeId == apps.Id).Fill, model.Fill(apps));
    }

    // MARK: Recycle Bin

    [Fact]
    public async Task Should_RecycleAnItemAndUpdateTheTreeWithoutRescanning_When_Asked()
    {
        var model = await ModelFixture.Scanned(fixture.Make());
        var apps = Child("Apps", model.CurrentFolder);

        model.Recycle(apps);

        Assert.Equal([apps.Path], fixture.Bin.Recycled);
        Assert.Equal(400, model.Tree!.AllocatedSize);
        Assert.Equal(["Docs", "movie.mov"], model.Rows.Select(row => row.Node.Name));
        Assert.Equal(["Apps"], fixture.Ledger.Records.Select(record => record.Name));
        Assert.Equal(600, fixture.Ledger.TotalSize);
        Assert.Equal("Apps", model.LastRecycled!.Name);
    }

    [Fact]
    public async Task Should_PutTheItemBack_When_UndoIsChosen()
    {
        var model = await ModelFixture.Scanned(fixture.Make());
        var reports = model.NodeById(Id(@"Docs\Reports"))!.Value;

        model.Recycle(reports);
        Assert.Equal(800, model.Tree!.AllocatedSize);
        fixture.Undo.Undo();

        Assert.Equal([reports.Path], fixture.Bin.PutBackPaths);
        Assert.Equal(1_000, model.Tree!.AllocatedSize);
        Assert.Empty(fixture.Ledger.Records);
        Assert.Null(model.LastRecycled);
    }

    [Fact]
    public async Task Should_MoveRecycledSpaceIntoOtherUsedSpace_When_TheWholeVolumeWasScanned()
    {
        var model = await ModelFixture.Scanned(fixture.Make(wholeVolume: true));

        model.Recycle(Child("Apps", model.CurrentFolder));

        Assert.Equal(1_200, model.Tree!.AllocatedSize);
        Assert.Equal(800, model.Tree.Children.Single(child => child.Kind == NodeKind.Unattributed).AllocatedSize);
    }

    [Fact]
    public async Task Should_GoToTheEnclosingFolder_When_TheOpenFolderIsRecycled()
    {
        var model = await ModelFixture.Scanned(fixture.Make());
        model.Open(Child("Docs", model.CurrentFolder));

        model.Recycle(model.CurrentFolder);

        Assert.Single(model.Path);
        Assert.Equal(["Apps", "movie.mov"], model.Rows.Select(row => row.Node.Name));
    }

    [Fact]
    public async Task Should_ReportAFailedMoveAndLeaveTheTree_When_TheBinRefuses()
    {
        var model = await ModelFixture.Scanned(fixture.Make());
        fixture.Bin.Failure = new RecycleException(RecycleFailure.NoPermission, "Apps");

        model.Recycle(Child("Apps", model.CurrentFolder));

        Assert.Contains("Apps", model.ActionError!.Message, StringComparison.Ordinal);
        Assert.Equal(1_000, model.Tree!.AllocatedSize);
        Assert.Empty(fixture.Ledger.Records);
    }

    [Fact]
    public async Task Should_OnlyOfferRecyclingForRealItemsBelowTheLocation_When_Asked()
    {
        var model = await ModelFixture.Scanned(fixture.Make(wholeVolume: true));
        var other = model.CurrentFolder!.Value.Children.Single(child => child.Node.Kind == NodeKind.Unattributed);

        Assert.False(model.CanRecycle(model.CurrentFolder));
        Assert.False(model.CanRecycle(other));
        Assert.True(model.CanRecycle(Child("movie.mov", model.CurrentFolder)));
    }

    [Fact]
    public async Task Should_RefuseSystemLocations_When_TheScanCoversTheWindowsFolder()
    {
        var root = FileNode.Directory(@"C:\", null, [Dir("Windows", Dir("System32", File("ntoskrnl.exe", 20_000_000))), Dir("Games", File("a.pak", 20_000_000))]);
        var model = await ModelFixture.Scanned(fixture.Make([Finished(root)], root: @"C:\", rules: []));

        Assert.False(model.CanRecycle(Child("Windows", model.CurrentFolder)));
        Assert.True(model.CanRecycle(Child("Games", model.CurrentFolder)));
        model.Recycle(Child("Windows", model.CurrentFolder));
        Assert.Empty(fixture.Bin.Recycled);
    }

    // MARK: Cleanup rules

    /// <summary>C:\Users\dev with NuGet's HTTP cache (safe, 700) and Store app data (keep, 300).</summary>
    private LocationScanModel ModelWithCaches()
    {
        var root = FileNode.Directory(@"C:\Users\dev", null,
        [
            Dir("AppData", Dir("Local",
                Dir("NuGet", Dir("v3-cache", File("App", 700))),
                Dir("Packages", File("A", 300)))),
        ]);
        return fixture.Make([Finished(root)], root: @"C:\Users\dev");
    }

    [Fact]
    public async Task Should_FindArtifactsLargestFirst_When_AScanFinishes()
    {
        var model = await ModelFixture.Scanned(ModelWithCaches());

        Assert.Equal(["dotnet.httpCache", "appData.packages"], model.CleanupMatches.Select(match => match.Rule.Id));
        Assert.True(model.CanHighlightReclaimable);
        var cache = model.CleanupMatches[0];
        Assert.Equal(SafetyLevel.Safe, model.CleanupMatchFor(new NodeRef(cache.Node, cache.Path))!.Rule.Safety);
    }

    [Fact]
    public async Task Should_NeverRecycleKeptItemsAndColorBySafety_When_Highlighting()
    {
        var model = await ModelFixture.Scanned(ModelWithCaches());
        var kept = model.CleanupMatches.Single(match => match.Rule.Safety == SafetyLevel.Keep);

        model.Recycle(kept);
        Assert.Empty(fixture.Bin.Recycled);

        model.IsHighlightingReclaimable = true;
        var cache = model.CleanupMatches.Single(match => match.Rule.Safety == SafetyLevel.Safe);
        Assert.Equal(SegmentFill.ForSafety(SafetyLevel.Safe), model.DisplayFill(new NodeRef(cache.Node, cache.Path)));
    }

    [Fact]
    public async Task Should_RecycleAMatchedCacheFromTheDeveloperViewAndDropItsMatch_When_Asked()
    {
        var model = await ModelFixture.Scanned(ModelWithCaches());
        var cache = model.CleanupMatches.Single(match => match.Rule.Safety == SafetyLevel.Safe);

        model.Recycle(cache);
        Assert.DoesNotContain(model.CleanupMatches, match => match.Id == cache.Id);
        await ModelFixture.Settle(model);

        Assert.Equal([cache.Path], fixture.Bin.Recycled);
        Assert.Equal(300, model.Tree!.AllocatedSize);
        Assert.Equal(["appData.packages"], model.CleanupMatches.Select(match => match.Rule.Id));
    }

    // MARK: History and large files

    [Fact]
    public async Task Should_SaveASnapshotAndShowGrowthAfterTheNextScan_When_ScanningTwice()
    {
        var first = await ModelFixture.Scanned(fixture.Make([Finished(Sample())]));
        Assert.Null(first.Growth);
        Assert.Single(fixture.Snapshots.All);

        var bigger = Root(Dir("Apps", File("Big.app", 50_000_000_500), File("Small.app", 100)),
            Dir("Docs", Dir("Reports", File("q1.pdf", 200)), File("notes.txt", 100)), File("movie.mov", 100));
        fixture.Now = fixture.Now.AddDays(1);
        var second = await ModelFixture.Scanned(fixture.Make([Finished(bigger)]));

        var apps = second.Rows.Single(row => row.Node.Name == "Apps");
        Assert.Equal(GrowthKind.Grew, second.GrowthFor(apps)?.Kind);
        Assert.EndsWith(@"\Apps\Big.app", second.Growth!.BiggestChanges()[0].Path, StringComparison.Ordinal);
        Assert.Equal(2, fixture.Snapshots.All.Count);
    }

    [Fact]
    public async Task Should_OpenTheFolderThatHoldsAChangedItemAndSelectIt_When_ShowingIt()
    {
        var model = await ModelFixture.Scanned(fixture.Make());

        model.ShowItem(Id(@"Docs\notes.txt"));

        Assert.Equal("Docs", model.CurrentFolder!.Value.Node.Name);
        Assert.Equal("notes.txt", model.Selection!.Value.Node.Name);
    }

    [Fact]
    public async Task Should_ListLargestFilesAndRecycleOneFromThatList_When_Asked()
    {
        var model = await ModelFixture.Scanned(fixture.Make());
        Assert.Equal("Big.app", model.LargeFiles[0].Node.Name);
        var q1 = model.LargeFiles.Single(file => file.Node.Name == "q1.pdf");

        model.Recycle(q1);

        Assert.Equal([q1.Path], fixture.Bin.Recycled);
        Assert.DoesNotContain(model.LargeFiles, file => file.Id == q1.Id);
        Assert.Equal(800, model.Tree!.AllocatedSize);
    }

    [Fact]
    public async Task Should_NotOfferRecyclingALargeFile_When_ItIsInsideAKeptFolder()
    {
        var model = await ModelFixture.Scanned(ModelWithCaches());
        var kept = model.LargeFiles.Single(file => file.Node.Name == "A");
        var cache = model.LargeFiles.Single(file => file.Node.Name == "App");

        Assert.False(model.CanRecycle(kept));
        Assert.True(model.CanRecycle(cache));
        model.Recycle(kept);
        Assert.Empty(fixture.Bin.Recycled);
    }

    [Fact]
    public async Task Should_CompareTheTwoSavedScans_When_NothingWasScannedThisSession()
    {
        const long Mb = 1_000_000;
        fixture.Snapshots.Save(new ScanSnapshot(1, RootPath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new Dictionary<string, long> { [Id("Apps")] = 100 * Mb }, new HashSet<string>()));
        fixture.Snapshots.Save(new ScanSnapshot(1, RootPath, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), new Dictionary<string, long> { [Id("Apps")] = 400 * Mb }, new HashSet<string>()));
        var model = fixture.Make();

        model.LoadSavedGrowth();
        await model.HistoryTask!;

        Assert.True(model.GrowthIsFromSavedScans);
        Assert.Equal(300 * Mb, model.Growth!.ChangeFor(Id("Apps"))!.Delta);
    }

    [Fact]
    public async Task Should_ReplaceTheSavedComparison_When_AScanFinishes()
    {
        var model = fixture.Make();
        model.LoadSavedGrowth();
        await model.HistoryTask!;

        await ModelFixture.Scanned(model);

        Assert.False(model.GrowthIsFromSavedScans);
    }

    [Fact]
    public async Task Should_RecycleItemsDroppedOnTheBinAndIgnoreOutsiders_When_Dropped()
    {
        var model = await ModelFixture.Scanned(fixture.Make());
        var apps = Child("Apps", model.CurrentFolder);

        bool moved = model.RecycleDropped([apps.Path, @"D:\Elsewhere\thing"]);

        Assert.True(moved);
        Assert.Equal([apps.Path], fixture.Bin.Recycled);
        Assert.Equal(["Docs", "movie.mov"], model.Rows.Select(row => row.Node.Name));
    }

    [Fact]
    public async Task Should_RefuseADrop_When_ARuleProtectsTheItem()
    {
        var model = await ModelFixture.Scanned(ModelWithCaches());
        var kept = model.CleanupMatches.Single(match => match.Rule.Safety == SafetyLevel.Keep);

        Assert.False(model.RecycleDropped([kept.Path]));
        Assert.Empty(fixture.Bin.Recycled);
    }

    [Fact]
    public async Task Should_ShowNoChange_When_ARowStandsForTheLocationItself()
    {
        var model = await ModelFixture.Scanned(fixture.Make(wholeVolume: true));
        var other = model.Rows.Single(row => row.Node.Kind == NodeKind.Unattributed);

        Assert.Null(model.GrowthFor(other));
    }

    // MARK: Cached scans

    [Fact]
    public async Task Should_ShowTheSavedScanWithoutReadingTheDisk_When_ReopeningTheLocation()
    {
        var first = await ModelFixture.Scanned(fixture.Make());
        Assert.Equal([RootPath], fixture.Cache.SavedLocations);

        // A model whose scanner would fail: everything on screen has to come from the cache.
        var reopened = fixture.Make([], new ScanException(ScanFailure.RootNotFound, RootPath));
        reopened.LoadCachedScan();
        await reopened.CacheTask!;

        Assert.True(reopened.HasResult);
        Assert.Equal(first.Rows.Select(row => row.Node.Name), reopened.Rows.Select(row => row.Node.Name));
        Assert.NotNull(reopened.ScannedAtUtc);
        Assert.Null(reopened.Result);
    }

    [Fact]
    public async Task Should_RefreshOnlyOnceTheSavedScanIsOlderThanThePeriod_When_Checking()
    {
        var model = await ModelFixture.Scanned(fixture.Make());
        var twoDaysLater = model.ScannedAtUtc!.Value.AddDays(2);

        Assert.True(model.NeedsRefresh(RefreshPeriod.Day, twoDaysLater));
        Assert.False(model.NeedsRefresh(RefreshPeriod.ThreeDays, twoDaysLater));
        Assert.False(model.NeedsRefresh(RefreshPeriod.Never, twoDaysLater.AddDays(3_000)));
    }

    [Fact]
    public async Task Should_KeepTheOpenFolderDuringARefreshAndReturnToIt_When_TheRefreshFinishes()
    {
        var model = await ModelFixture.Scanned(fixture.Make());
        model.Open(Child("Docs", model.CurrentFolder));

        model.StartScan(refreshing: true);
        Assert.True(model.IsRefreshing);
        Assert.Equal("Docs", model.CurrentFolder!.Value.Node.Name);
        await model.ScanTask!;

        Assert.False(model.IsRefreshing);
        Assert.Equal("Docs", model.CurrentFolder!.Value.Node.Name);
        Assert.NotNull(model.Result);
    }

    [Fact]
    public async Task Should_FindEveryRowAndEveryDrawnSegment_When_AFolderHoldsTensOfThousandsOfItems()
    {
        // A WinSxS-like folder: one big item and 40,000 tiny ones that the chart merges into a sliver.
        var tiny = Enumerable.Range(0, 40_000).Select(index => Dir($"c{index}", File("f", 1)));
        var root = Root(Dir("Windows", [Dir("WinSxS", [Dir("big", File("b", 10_000_000)), .. tiny])]), File("pagefile.sys", 5_000_000));
        var model = await ModelFixture.Scanned(fixture.Make([Finished(root)]));

        var windows = model.Rows.Single(row => row.Node.Name == "Windows");
        var winSxS = Assert.IsType<NodeRef>(model.NodeById(Id(@"Windows\WinSxS")));
        var big = model.NodeById(Id(@"Windows\WinSxS\big"));
        var sliver = model.NodeById(Id(@"Windows\WinSxS\c7"));

        Assert.All(model.Rows, row => Assert.NotNull(model.NodeById(row.Id)));
        Assert.Equal("WinSxS", winSxS.Node.Name);
        Assert.NotNull(big);
        Assert.Null(sliver);
        model.Open(windows);
        Assert.Equal(40_001, model.CurrentFolder!.Value.Children.Single(child => child.Node.Name == "WinSxS").Node.Children.Count);
    }

    private async Task<LocationScanModel> ScannedDrive()
    {
        var drive = FileNode.Directory(@"C:\", null,
        [
            Dir("Program Files", Dir("OldGame", File("data.pak", 900))),
            Dir("Windows", File("explorer.exe", 50)),
            Dir("Games", File("save.dat", 50)),
        ]);
        return await ModelFixture.Scanned(fixture.Make([Finished(drive)], root: @"C:\"));
    }

    [Fact]
    public async Task Should_AskBeforeMoving_When_TheItemIsInsideProgramFiles()
    {
        var model = await ScannedDrive();
        model.Open(model.Rows.Single(row => row.Node.Name == "Program Files"));
        var game = model.Rows.Single(row => row.Node.Name == "OldGame");

        model.Recycle(game);

        Assert.Empty(fixture.Bin.Recycled);
        Assert.Equal(@"C:\Program Files", model.PendingRecycle?.Area);
        Assert.Equal(game.Id, model.PendingRecycle?.Item.Id);
    }

    [Fact]
    public async Task Should_MoveTheItem_When_ThePersonConfirms()
    {
        var model = await ScannedDrive();
        model.Open(model.Rows.Single(row => row.Node.Name == "Program Files"));
        model.Recycle(model.Rows.Single(row => row.Node.Name == "OldGame"));

        model.ConfirmRecycle();

        Assert.Equal([@"C:\Program Files\OldGame"], fixture.Bin.Recycled);
        Assert.Null(model.PendingRecycle);
        Assert.DoesNotContain(model.Rows, row => row.Node.Name == "OldGame");
    }

    [Fact]
    public async Task Should_LeaveTheItem_When_ThePersonCancels()
    {
        var model = await ScannedDrive();
        model.Open(model.Rows.Single(row => row.Node.Name == "Program Files"));
        model.Recycle(model.Rows.Single(row => row.Node.Name == "OldGame"));

        model.CancelRecycle();

        Assert.Empty(fixture.Bin.Recycled);
        Assert.Null(model.PendingRecycle);
        Assert.Contains(model.Rows, row => row.Node.Name == "OldGame");
    }

    [Fact]
    public async Task Should_NeverOfferToMove_When_WindowsOrAProgramFolderItselfIsChosen()
    {
        var model = await ScannedDrive();
        var programFiles = model.Rows.Single(row => row.Node.Name == "Program Files");
        var windows = model.Rows.Single(row => row.Node.Name == "Windows");

        model.Recycle(programFiles);
        model.Recycle(windows);

        Assert.False(model.CanRecycle(programFiles));
        Assert.True(model.IsSystemProtected(windows));
        Assert.Null(model.PendingRecycle);
        Assert.Empty(fixture.Bin.Recycled);
        Assert.True(model.CanRecycle(model.Rows.Single(row => row.Node.Name == "Games")));
    }
}
