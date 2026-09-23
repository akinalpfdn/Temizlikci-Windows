using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Actions;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;
using Temizlikci.Tests.Support;

namespace Temizlikci.Tests.Presentation;

internal sealed class StubVolumes(params VolumeDescription[] volumes) : IVolumeInfoProvider
{
    public bool FailsUsage { get; init; }

    public IReadOnlyList<VolumeDescription> FixedVolumes() => volumes;

    public VolumeDescription? VolumeContaining(string path) => volumes.FirstOrDefault(v => path.StartsWith(v.RootPath, StringComparison.OrdinalIgnoreCase));

    public VolumeUsage Usage(string path) => FailsUsage ? throw new IOException("offline") : new VolumeUsage(500L << 30, 120L << 30);
}

internal sealed class StubElevation(bool elevated = false) : IElevation
{
    public int Restarts { get; private set; }

    public bool IsElevated => elevated;

    public bool RestartElevated()
    {
        Restarts++;
        return true;
    }
}

internal sealed class MemorySettings : ISettingsStore
{
    public AppSettings Stored { get; private set; } = new();

    public AppSettings Load() => Stored;

    public void Save(AppSettings settings) => Stored = settings;
}

internal sealed class StubPicker(string? folder) : IFolderPicker
{
    public Task<string?> PickFolderAsync() => Task.FromResult(folder);
}

public sealed class MainViewModelTests
{
    private static readonly VolumeDescription SystemDrive = new(@"C:\", "Windows", "NTFS", IsFixed: true, IsSystem: true);
    private static readonly VolumeDescription DataDrive = new(@"D:\", "", "NTFS", IsFixed: true, IsSystem: false);

    private readonly ModelFixture fixture = new();
    private readonly MemorySettings settings = new();

    private MainViewModel Make(IVolumeInfoProvider? volumes = null, bool elevated = false, string? picked = null, IReadOnlyList<ScanEvent>? events = null) => new(
        volumes ?? new StubVolumes(SystemDrive, DataDrive),
        @"C:\Users\akina",
        location => fixture.Make(events, root: location.Path, wholeVolume: location.IsWholeVolume),
        new StubPicker(picked),
        settings,
        new StubElevation(elevated),
        fixture.Ledger,
        fixture.Undo);

    [Fact]
    public void Should_OpenTheSystemDrive_When_TheAppStarts()
    {
        Assert.Equal(SidebarDestination.Drive(@"C:\"), Make().Selection);
    }

    [Fact]
    public void Should_ListEveryFixedDriveThenHome_When_BuildingLocations()
    {
        Assert.Equal([SidebarDestination.Drive(@"C:\"), SidebarDestination.Drive(@"D:\"), SidebarDestination.Home], Make().LocationDestinations);
    }

    [Fact]
    public void Should_NameADriveLikeExplorer_When_ItHasALabelOrNot()
    {
        var model = Make();

        Assert.Equal("Windows (C:)", model.Title(SidebarDestination.Drive(@"C:\")));
        Assert.Equal("Local Disk (D:)", model.Title(SidebarDestination.Drive(@"D:\")));
    }

    [Fact]
    public void Should_ShowFreeSpace_When_ADriveCanReportIt()
    {
        Assert.Equal("120 GB free", Make().Badge(SidebarDestination.Drive(@"C:\")));
        Assert.Null(Make(new StubVolumes(SystemDrive) { FailsUsage = true }).Badge(SidebarDestination.Drive(@"C:\")));
        Assert.Null(Make().Badge(SidebarDestination.Developer));
    }

    [Fact]
    public void Should_CreateAScanPerLocation_When_Starting()
    {
        var model = Make();

        Assert.Equal(@"C:\", model.ScanModel(SidebarDestination.Drive(@"C:\"))!.Location.Path);
        Assert.True(model.ScanModel(SidebarDestination.Drive(@"C:\"))!.Location.IsWholeVolume);
        Assert.False(model.ScanModel(SidebarDestination.Home)!.Location.IsWholeVolume);
        Assert.Null(model.ScanModel(SidebarDestination.Developer));
    }

    [Fact]
    public async Task Should_AddAndOpenTheFolder_When_OneIsChosen()
    {
        var model = Make(picked: @"C:\Users\akina\source");

        await model.ChooseFolderAsync();

        Assert.Equal(SidebarDestination.ChosenFolder, model.Selection);
        Assert.Contains(SidebarDestination.ChosenFolder, model.LocationDestinations);
        Assert.Equal("source", model.Title(SidebarDestination.ChosenFolder));
        Assert.Equal(@"C:\Users\akina\source", model.CurrentScan!.Location.Path);
    }

    [Fact]
    public async Task Should_ChangeNothing_When_ThePickerIsCancelled()
    {
        var model = Make(picked: null);

        await model.ChooseFolderAsync();

        Assert.DoesNotContain(SidebarDestination.ChosenFolder, model.LocationDestinations);
        Assert.Equal(SidebarDestination.Drive(@"C:\"), model.Selection);
    }

    [Fact]
    public void Should_ForgetTheInspectedItem_When_AnotherDestinationOpens()
    {
        var model = Make();
        model.Selection = SidebarDestination.Developer;
        model.Inspected = new InspectedItem(@"C:\x", IsProject: false);

        model.Selection = SidebarDestination.LargeFiles;

        Assert.Null(model.Inspected);
    }

    [Fact]
    public async Task Should_OfferRestartingAsAdministrator_When_AScanLeftFoldersUnread()
    {
        var unread = new ScanEvent.Finished(new ScanResult(TreeBuilder.Sample(), TimeSpan.Zero, 5, 3, InaccessibleCount: 2, ScanMethod.DirectoryWalk));
        var model = Make(events: [unread]);
        var scan = await ModelFixture.Scanned(model.CurrentScan!);

        Assert.True(model.ShouldShowAccessBanner(scan));
        model.IsAccessBannerDismissed = true;
        Assert.False(model.ShouldShowAccessBanner(scan));
        Assert.False(Make(elevated: true, events: [unread]).ShouldShowAccessBanner(scan));
    }

    [Fact]
    public async Task Should_UseTheFirstScannedLocation_When_AnInsightViewNeedsAScan()
    {
        var model = Make();
        Assert.Null(model.DeveloperScan);

        var home = model.ScanModel(SidebarDestination.Home)!;
        await ModelFixture.Scanned(home);

        Assert.Same(home, model.DeveloperScan);
        model.Selection = SidebarDestination.LargeFiles;
        Assert.Same(home, model.InsightScan);
    }

    [Fact]
    public void Should_RememberSettings_When_TheyChange()
    {
        var model = Make();

        model.Settings = model.Settings with { RefreshPeriod = Temizlikci.Domain.History.RefreshPeriod.Week };

        Assert.Equal(Temizlikci.Domain.History.RefreshPeriod.Week, settings.Stored.RefreshPeriod);
    }

    [Fact]
    public async Task Should_PutAnItemBackThroughItsLocation_When_TheRecycleBinViewAsks()
    {
        var model = Make();
        var home = model.ScanModel(SidebarDestination.Home)!;
        var fixtureRoot = await ModelFixture.Scanned(home);
        var apps = fixtureRoot.CurrentFolder!.Value.Children.Single(child => child.Node.Name == "Apps");
        home.Recycle(apps);
        Assert.Single(model.Ledger.Records);

        model.PutBack(model.Ledger.Records[0]);

        Assert.Empty(model.Ledger.Records);
        Assert.Equal([apps.Path], fixture.Bin.PutBackPaths);
    }

    [Fact]
    public async Task Should_ReturnTheErrorAndKeepTheRecord_When_PutBackFails()
    {
        var model = Make();
        var home = model.ScanModel(SidebarDestination.Home)!;
        await ModelFixture.Scanned(home);
        home.Recycle(home.CurrentFolder!.Value.Children.Single(child => child.Node.Name == "Apps"));
        fixture.Bin.PutBackFailure = new RecycleException(RecycleFailure.Occupied, "Apps");

        var error = model.PutBack(model.Ledger.Records[0]);

        Assert.NotNull(error);
        Assert.Equal(L10n.RecycleErrorOccupied("Apps"), error.Message);
        Assert.Single(model.Ledger.Records);
        Assert.Null(home.ActionError);
    }

    [Fact]
    public async Task Should_DropItemsEmptiedFromTheRecycleBin_When_TheWindowIsActivated()
    {
        var model = Make();
        var home = model.ScanModel(SidebarDestination.Home)!;
        await ModelFixture.Scanned(home);
        var apps = home.CurrentFolder!.Value.Children.Single(child => child.Node.Name == "Apps");
        home.Recycle(apps);
        fixture.Bin.GoneFromBin.Add(apps.Path);

        await model.ActivatedAsync();

        Assert.Empty(model.Ledger.Records);
        Assert.Null(model.Badge(SidebarDestination.RecycleBin));
    }
}
