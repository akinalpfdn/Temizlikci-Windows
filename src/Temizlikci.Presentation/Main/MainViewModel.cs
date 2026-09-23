using CommunityToolkit.Mvvm.ComponentModel;
using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.Presentation.Main;

/// <summary>The window: which sidebar destination is open and the chrome around it.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IVolumeInfoProvider volumes;

    [ObservableProperty]
    private SidebarDestination? selection;

    [ObservableProperty]
    private bool isInspectorPresented = true;

    public MainViewModel(IVolumeInfoProvider volumes, string homeFolder)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        this.volumes = volumes;
        HomeFolder = homeFolder;
        Drives = volumes.FixedVolumes();
        LocationDestinations = Drives.Select(drive => SidebarDestination.Drive(drive.RootPath))
            .Append(SidebarDestination.Home)
            .ToList();
        selection = LocationDestinations[0];
    }

    public string HomeFolder { get; }

    public IReadOnlyList<VolumeDescription> Drives { get; }

    public IReadOnlyList<SidebarDestination> LocationDestinations { get; }

    public IReadOnlyList<SidebarDestination> InsightDestinations { get; } =
    [
        SidebarDestination.Developer,
        SidebarDestination.WhatGrew,
        SidebarDestination.LargeFiles,
        SidebarDestination.RecycleBin,
    ];

    public string Title(SidebarDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return destination.Kind switch
        {
            DestinationKind.Drive => DriveTitle(destination.Path),
            DestinationKind.Home => L10n.SidebarHome,
            DestinationKind.ChosenFolder => L10n.SidebarChooseFolder,
            DestinationKind.Developer => L10n.SidebarDeveloper,
            DestinationKind.WhatGrew => L10n.SidebarWhatGrew,
            DestinationKind.LargeFiles => L10n.SidebarLargeFiles,
            DestinationKind.RecycleBin => L10n.SidebarRecycleBin,
            _ => L10n.AppName,
        };
    }

    /// <summary>The badge beside a drive: its free space. <c>null</c> when it can't be read or isn't a drive.</summary>
    public string? Badge(SidebarDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Kind != DestinationKind.Drive || destination.Path is null) return null;
        try
        {
            return L10n.VolumeFree(Format.Bytes(volumes.Usage(destination.Path).AvailableCapacity));
        }
        catch (IOException)
        {
            // A drive that can't report its space just shows no badge.
            return null;
        }
    }

    private string DriveTitle(string? rootPath)
    {
        var drive = Drives.FirstOrDefault(volume => string.Equals(volume.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        if (drive is null) return rootPath ?? L10n.SidebarLocalDiskFallback;
        string label = string.IsNullOrWhiteSpace(drive.Label) ? L10n.SidebarLocalDiskFallback : drive.Label;
        return L10n.DriveName(label, drive.Letter);
    }
}
