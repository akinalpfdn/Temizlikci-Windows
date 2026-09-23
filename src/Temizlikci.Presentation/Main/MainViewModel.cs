using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Actions;
using Temizlikci.Presentation.Developer;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.Presentation.Main;

/// <summary>An item picked in an insight view (Developer, Large Files, What Grew), shown in the inspector.</summary>
public sealed record InspectedItem(string Id, bool IsProject);

/// <summary>The window: which sidebar destination is open, one scan model per location, and the chrome around them.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IVolumeInfoProvider volumes;
    private readonly Func<ScanLocation, LocationScanModel> makeScanModel;
    private readonly IFolderPicker folderPicker;
    private readonly ISettingsStore settingsStore;
    private readonly Dictionary<SidebarDestination, LocationScanModel> scanModels = [];
    private SidebarDestination? selection;
    private AppSettings settings;

    [ObservableProperty]
    private bool isInspectorPresented = true;

    [ObservableProperty]
    private bool isSidebarPresented = true;

    [ObservableProperty]
    private InspectedItem? inspected;

    [ObservableProperty]
    private bool isAccessBannerDismissed;

    public MainViewModel(
        IVolumeInfoProvider volumes,
        string homeFolder,
        Func<ScanLocation, LocationScanModel> makeScanModel,
        IFolderPicker folderPicker,
        ISettingsStore settingsStore,
        IElevation elevation,
        RecycleLedger ledger,
        UndoHistory undo,
        InsightTools tools)
    {
        this.volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));
        this.makeScanModel = makeScanModel ?? throw new ArgumentNullException(nameof(makeScanModel));
        this.folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        Elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
        Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        Undo = undo ?? throw new ArgumentNullException(nameof(undo));
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
        settings = settingsStore.Load();
        HomeFolder = homeFolder;
        Drives = volumes.FixedVolumes();
        foreach (var drive in Drives)
        {
            AddScanModel(SidebarDestination.Drive(drive.RootPath), new ScanLocation(drive.RootPath, DriveTitle(drive.RootPath), IsWholeVolume: true));
        }
        AddScanModel(SidebarDestination.Home, new ScanLocation(homeFolder, L10n.SidebarHome, IsWholeVolume: false));
        var locations = LocationDestinations;
        selection = locations.Count > 0 ? locations[0] : null;
        Ledger.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Ledger));
        Tools.Windows.PropertyChanged += OnWindowsToolsChanged;
    }

    public string HomeFolder { get; }

    public IReadOnlyList<VolumeDescription> Drives { get; }

    public IElevation Elevation { get; }

    public RecycleLedger Ledger { get; }

    public UndoHistory Undo { get; }

    public InsightTools Tools { get; }

    public AppSettings Settings
    {
        get => settings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref settings, value)) return;
            settingsStore.Save(value);
        }
    }

    public string? ChosenFolder { get; private set; }

    public SidebarDestination? Selection
    {
        get => selection;
        set
        {
            if (!SetProperty(ref selection, value)) return;
            // What the inspector shows for an insight view belongs to that view only.
            Inspected = null;
            OnPropertyChanged(nameof(CurrentScan));
        }
    }

    public IReadOnlyList<SidebarDestination> LocationDestinations
    {
        get
        {
            var destinations = Drives.Select(drive => SidebarDestination.Drive(drive.RootPath)).Append(SidebarDestination.Home);
            return ChosenFolder is null ? destinations.ToList() : destinations.Append(SidebarDestination.ChosenFolder).ToList();
        }
    }

    public IReadOnlyList<SidebarDestination> InsightDestinations { get; } =
    [
        SidebarDestination.Developer,
        SidebarDestination.WhatGrew,
        SidebarDestination.LargeFiles,
        SidebarDestination.RecycleBin,
    ];

    /// <summary>The scan model behind the selected location, if a location is selected.</summary>
    public LocationScanModel? CurrentScan => Selection is { } destination ? ScanModel(destination) : null;

    public LocationScanModel? ScanModel(SidebarDestination destination) => scanModels.GetValueOrDefault(destination);

    public IEnumerable<LocationScanModel> AllScans => LocationDestinations.Select(ScanModel).OfType<LocationScanModel>();

    public string Title(SidebarDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return destination.Kind switch
        {
            DestinationKind.Drive => DriveTitle(destination.Path),
            DestinationKind.Home => L10n.SidebarHome,
            DestinationKind.ChosenFolder => ChosenFolder is { } folder ? Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder : L10n.SidebarChooseFolder,
            DestinationKind.Developer => L10n.SidebarDeveloper,
            DestinationKind.WhatGrew => L10n.SidebarWhatGrew,
            DestinationKind.LargeFiles => L10n.SidebarLargeFiles,
            DestinationKind.RecycleBin => L10n.SidebarRecycleBin,
            _ => L10n.AppName,
        };
    }

    /// <summary>The badge beside a sidebar row: a drive's free space, or what this session moved to the Recycle Bin.</summary>
    public string? Badge(SidebarDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Kind == DestinationKind.RecycleBin) return Ledger.TotalSize > 0 ? Format.Bytes(Ledger.TotalSize) : null;
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

    /// <summary>The window's subtitle: what the selected location holds, or how far its scan has got.</summary>
    public string Subtitle
    {
        get
        {
            if (CurrentScan is not { } scan) return string.Empty;
            return scan.Phase switch
            {
                ScanPhase.Scanning => L10n.NavigationScanningSubtitle(Format.Bytes(scan.Progress.AllocatedSize)),
                ScanPhase.Finished when scan.Usage is { } usage && scan.Path.Count == 1 =>
                    L10n.NavigationVolumeSubtitle(Format.Bytes(usage.UsedCapacity), Format.Bytes(usage.AvailableCapacity)),
                ScanPhase.Finished => Format.Bytes(scan.CurrentFolder?.Node.AllocatedSize ?? 0),
                _ => L10n.NavigationNoScanSubtitle,
            };
        }
    }

    public async Task ChooseFolderAsync()
    {
        if (await folderPicker.PickFolderAsync().ConfigureAwait(true) is { } folder) OpenFolder(folder);
    }

    /// <summary>Adds <paramref name="folder"/> as the chosen location, replacing the previous one, and opens it.</summary>
    public void OpenFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (scanModels.Remove(SidebarDestination.ChosenFolder, out var previous))
        {
            previous.PropertyChanged -= OnScanChanged;
            previous.Dispose();
        }
        ChosenFolder = folder;
        AddScanModel(SidebarDestination.ChosenFolder, new ScanLocation(folder, Title(SidebarDestination.ChosenFolder), IsWholeVolume: false));
        OnPropertyChanged(nameof(LocationDestinations));
        Selection = SidebarDestination.ChosenFolder;
    }

    /// <summary>Every location opens its saved scan straight away, so the chart is there before anyone asks.</summary>
    private void AddScanModel(SidebarDestination destination, ScanLocation location)
    {
        var model = makeScanModel(location);
        model.PropertyChanged += OnScanChanged;
        scanModels[destination] = model;
        model.LoadCachedScan();
    }

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, CurrentScan)) OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Scans));
    }

    /// <summary>Raised whenever any location's scan changes; insight views listen to it.</summary>
    public object Scans => scanModels;

    /// <summary>Shows the saved scan and refreshes it when it's older than the chosen period.</summary>
    public async Task OpenLocationAsync(LocationScanModel scan, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(scan);
        scan.LoadCachedScan();
        if (scan.CacheTask is { } loading) await loading.ConfigureAwait(true);
        if (scan.NeedsRefresh(Settings.RefreshPeriod, nowUtc)) scan.StartScan(refreshing: true);
    }

    // MARK: Insights

    /// <summary>The scan the Developer view summarizes: the first drive with results, otherwise Home.</summary>
    public LocationScanModel? DeveloperScan => AllScans.FirstOrDefault(scan => scan.HasResult);

    /// <summary>The scan Large Files lists: the first location with results.</summary>
    public LocationScanModel? LargeFilesScan => AllScans.FirstOrDefault(scan => scan.HasResult);

    /// <summary>The scan What Grew compares: the first location with a comparison from this session, else any.</summary>
    public LocationScanModel? GrowthScan =>
        AllScans.FirstOrDefault(scan => scan.Growth is not null && !scan.GrowthIsFromSavedScans) ?? AllScans.FirstOrDefault(scan => scan.Growth is not null);

    /// <summary>The scan behind the insight view on screen, so the inspector can look items up in it.</summary>
    public LocationScanModel? InsightScan => Selection?.Kind switch
    {
        DestinationKind.Developer => DeveloperScan,
        DestinationKind.LargeFiles => LargeFilesScan,
        DestinationKind.WhatGrew => GrowthScan,
        _ => null,
    };

    /// <summary>Fills What Grew from saved scans when nothing has been scanned in this session.</summary>
    public void LoadSavedGrowth()
    {
        foreach (var scan in AllScans) scan.LoadSavedGrowth();
    }

    /// <summary>Switches to the scan's location and opens the item at <paramref name="path"/> in the chart.</summary>
    public void Show(string path, LocationScanModel scan)
    {
        var destination = scanModels.FirstOrDefault(entry => ReferenceEquals(entry.Value, scan)).Key;
        if (destination is null) return;
        Selection = destination;
        scan.ShowItem(path);
    }

    public void ScanHome()
    {
        Selection = SidebarDestination.Home;
        CurrentScan?.StartScan();
    }

    // MARK: Windows tools

    public bool IsAndroidStudioInstalled => Tools.Editors.IsInstalled(Editor.AndroidStudio);

    /// <summary>Android Studio's Device Manager is where emulators and system images are deleted.</summary>
    public void OpenAndroidStudio() => Tools.Editors.Start(Editor.AndroidStudio);

    /// <summary>Settings › System › Storage, which removes temporary files, update leftovers and old installations.</summary>
    public void OpenStorageSettings() => Tools.Shell.OpenUri(new Uri("ms-settings:storagesense"));

    public void OpenSystemProtection() => Tools.Shell.OpenSystemProtection();

    private void OnWindowsToolsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WindowsToolsModel.DidChangeDisk) || !Tools.Windows.DidChangeDisk) return;
        ToolsChangedDisk();
        // Free space in the sidebar moved too.
        OnPropertyChanged(nameof(Drives));
    }

    /// <summary>After a tool (wsl, DISM) changed the disk, every finished scan's sizes are outdated until rescanned.</summary>
    public void ToolsChangedDisk()
    {
        foreach (var scan in AllScans) scan.MarkOutdated();
    }

    // MARK: Recycle Bin

    public bool CanRecycleSelection => CurrentScan is { } scan && scan.CanRecycle(scan.Selection ?? scan.CurrentFolder);

    public void RecycleSelection()
    {
        if (CurrentScan is { } scan) scan.Recycle(scan.Selection ?? scan.CurrentFolder);
    }

    /// <summary>Puts an item back through the location it came from, so that location's tree updates too. Returns
    /// the failure for the caller to show, instead of leaving it on a location that isn't visible.</summary>
    public ActionError? PutBack(RecycleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var owner = AllScans.FirstOrDefault(scan => string.Equals(scan.Location.Path.TrimEnd('\\'), record.LocationPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            ?? AllScans.First();
        owner.PutBack(record);
        if (owner.ActionError is not { } error) return null;
        owner.DismissError();
        return error;
    }

    public bool RecycleDropped(IReadOnlyList<string> paths) => CurrentScan?.RecycleDropped(paths) ?? false;

    // MARK: Administrator rights

    /// <summary>After a scan that couldn't read folders, unelevated: offer to restart as administrator (the Windows
    /// counterpart of the Full Disk Access banner).</summary>
    public bool ShouldShowAccessBanner(LocationScanModel scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return !Elevation.IsElevated && !IsAccessBannerDismissed && (scan.Result?.InaccessibleCount ?? 0) > 0;
    }

    /// <summary>Starts an elevated copy of the app; true when it started and this instance should close.</summary>
    public bool RestartAsAdministrator() => Elevation.RestartElevated();

    /// <summary>Called when the window comes back to the front: the Recycle Bin may have been emptied meanwhile.</summary>
    public async Task ActivatedAsync()
    {
        await Ledger.ReconcileAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(Drives));
    }

    private string DriveTitle(string? rootPath)
    {
        var drive = Drives.FirstOrDefault(volume => string.Equals(volume.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        if (drive is null) return rootPath ?? L10n.SidebarLocalDiskFallback;
        string label = string.IsNullOrWhiteSpace(drive.Label) ? L10n.SidebarLocalDiskFallback : drive.Label;
        return L10n.DriveName(label, drive.Letter);
    }

    public void Dispose()
    {
        foreach (var scan in scanModels.Values) scan.Dispose();
    }
}
