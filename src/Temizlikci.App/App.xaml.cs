using Microsoft.UI.Xaml;
using Temizlikci.App.Services;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Identity;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Updates;
using Temizlikci.Presentation.Actions;
using Temizlikci.Presentation.Developer;
using Temizlikci.Presentation.Intro;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Updates;
using Temizlikci.Services.Access;
using Temizlikci.Services.Editors;
using Temizlikci.Services.Identity;
using Temizlikci.Services.Persistence;
using Temizlikci.Services.Projects;
using Temizlikci.Services.RecycleBin;
using Temizlikci.Services.Scanning;
using Temizlikci.Services.Shell;
using Temizlikci.Services.Tools;
using Temizlikci.Services.Updates;
using Temizlikci.Services.Volumes;

namespace Temizlikci.App;

/// <summary>The composition root: the only place that creates services and hands them to view models.</summary>
public partial class App : Application
{
    private MainWindow? window;

    public App()
    {
        ApplyPreviewTheme();
        InitializeComponent();
    }

    /// <summary>Debug builds only: <c>--theme=light</c> or <c>--theme=dark</c> forces a theme for screenshots.</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void ApplyPreviewTheme()
    {
        string? argument = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith("--theme=", StringComparison.Ordinal));
        if (argument is not null) RequestedTheme = argument.EndsWith("dark", StringComparison.Ordinal) ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settingsStore = new JsonSettingsStore();
        var elevation = new WindowsElevation();
        bool restarted = Environment.GetCommandLineArgs().Contains(WindowsElevation.RestartedArgument);
        if (settingsStore.Load().StartAsAdministrator && !elevation.IsElevated && !restarted && elevation.RestartElevated())
        {
            Exit();
            return;
        }
        // Elevated, the scanner may read every folder whatever its permissions say, like a backup program.
        if (elevation.IsElevated) WindowsElevation.EnableBackupPrivilege();

        var locations = KnownLocations.FromEnvironment();
        var markers = new FileSystemMarkerChecker();
        var apps = new InstalledApps();
        apps.Warm();
        var recycleBin = new ShellRecycleBin(() => window is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(window));
        var ledger = new RecycleLedger(recycleBin);
        var undo = new UndoHistory();
        var volumes = new SystemVolumeInfo();
        var services = new LocationServices
        {
            Scanner = new AdaptiveScanner(() => elevation.IsElevated, new DirectoryScanner(), new MftScanner()),
            Volumes = volumes,
            Shell = new WindowsShell(),
            RecycleBin = recycleBin,
            Ledger = ledger,
            Undo = undo,
            Rules = new RuleEngine(CleanupCatalog.Rules, locations, markers),
            Projects = new ProjectFinder(markers, new FileSystemProjectActivity(), locations),
            Protection = new SystemProtection(locations),
            Identifier = new FolderIdentifier(locations, apps, new ShellFileTypeNames()),
            Cache = new FileScanCache(),
            Snapshots = new FileSnapshotStore(),
            HiddenSpace = new HiddenSpaceReader(),
        };

        var runner = new ProcessToolRunner();
        var windowsTools = new WindowsToolsModel(
            new WslManager(runner, elevation), new DismComponentStore(runner, elevation), volumes, Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
        var tools = new InsightTools(windowsTools, new GitStatusModel(new GitInspector(runner)), new WindowsEditorLauncher(), markers, services.Shell);

        var main = new MainViewModel(
            volumes,
            locations.Home,
            location => new LocationScanModel(location, services),
            new WindowsFolderPicker(() => window!.AppWindow.Id),
            settingsStore,
            elevation,
            ledger,
            undo,
            tools);
        var version = AppVersion.Parse(typeof(App).Assembly.GetName().Version?.ToString(3)) ?? AppVersion.Parse("0")!;
        var releases = new GitHubReleases(version.ToString());
        var updates = new UpdateModel(version, releases, () => main.Settings, settings => main.Settings = settings, services.Shell);
        var sampleServices = SampleLocation.Services(services, locations);
        window = new MainWindow(main, services, updates, () => new LocationScanModel(SampleLocation.Location, sampleServices));
        window.Closed += (_, _) =>
        {
            main.Dispose();
            releases.Dispose();
        };
        window.Activate();
        _ = updates.CheckIfDueAsync(DateTime.UtcNow);
    }
}
