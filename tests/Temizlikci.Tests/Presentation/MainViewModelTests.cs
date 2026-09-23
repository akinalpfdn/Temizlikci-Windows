using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Main;

namespace Temizlikci.Tests.Presentation;

public sealed class MainViewModelTests
{
    private sealed class StubVolumes(params VolumeDescription[] volumes) : IVolumeInfoProvider
    {
        public bool FailsUsage { get; init; }

        public IReadOnlyList<VolumeDescription> FixedVolumes() => volumes;

        public VolumeDescription? VolumeContaining(string path) => volumes.FirstOrDefault(v => path.StartsWith(v.RootPath, StringComparison.OrdinalIgnoreCase));

        public VolumeUsage Usage(string path) => FailsUsage ? throw new IOException("offline") : new VolumeUsage(500L << 30, 120L << 30);
    }

    private static readonly VolumeDescription SystemDrive = new(@"C:\", "Windows", "NTFS", IsFixed: true, IsSystem: true);
    private static readonly VolumeDescription DataDrive = new(@"D:\", "", "NTFS", IsFixed: true, IsSystem: false);

    [Fact]
    public void Should_OpenTheSystemDrive_When_TheAppStarts()
    {
        var model = new MainViewModel(new StubVolumes(SystemDrive, DataDrive), @"C:\Users\akina");
        Assert.Equal(SidebarDestination.Drive(@"C:\"), model.Selection);
    }

    [Fact]
    public void Should_ListEveryFixedDriveThenHome_When_BuildingLocations()
    {
        var model = new MainViewModel(new StubVolumes(SystemDrive, DataDrive), @"C:\Users\akina");
        Assert.Equal([SidebarDestination.Drive(@"C:\"), SidebarDestination.Drive(@"D:\"), SidebarDestination.Home], model.LocationDestinations);
    }

    [Fact]
    public void Should_NameADriveLikeExplorer_When_ItHasALabel()
    {
        var model = new MainViewModel(new StubVolumes(SystemDrive), @"C:\Users\akina");
        Assert.Equal("Windows (C:)", model.Title(SidebarDestination.Drive(@"C:\")));
    }

    [Fact]
    public void Should_CallItLocalDisk_When_ADriveHasNoLabel()
    {
        var model = new MainViewModel(new StubVolumes(SystemDrive, DataDrive), @"C:\Users\akina");
        Assert.Equal("Local Disk (D:)", model.Title(SidebarDestination.Drive(@"D:\")));
    }

    [Fact]
    public void Should_ShowFreeSpace_When_ADriveCanReportIt()
    {
        var model = new MainViewModel(new StubVolumes(SystemDrive), @"C:\Users\akina");
        Assert.Equal("120 GB free", model.Badge(SidebarDestination.Drive(@"C:\")));
    }

    [Fact]
    public void Should_ShowNoBadge_When_ADriveCantReportItsSpace()
    {
        var model = new MainViewModel(new StubVolumes(SystemDrive) { FailsUsage = true }, @"C:\Users\akina");
        Assert.Null(model.Badge(SidebarDestination.Drive(@"C:\")));
    }

    [Fact]
    public void Should_ShowNoBadge_When_TheDestinationIsNotADrive()
    {
        var model = new MainViewModel(new StubVolumes(SystemDrive), @"C:\Users\akina");
        Assert.Null(model.Badge(SidebarDestination.Developer));
    }
}
