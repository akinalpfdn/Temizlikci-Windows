using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.Identity;

/// <summary>Folders and files Windows itself defines. Each has an explanation in the presentation layer.</summary>
public enum KnownFolder
{
    SystemDrive,
    OtherDrive,
    Windows,
    System32,
    SysWow64,
    WinSxS,
    WindowsInstaller,
    SoftwareDistribution,
    WindowsTemp,
    ProgramFiles,
    ProgramFilesX86,
    ProgramData,
    Users,
    PublicProfile,
    Home,
    AppData,
    LocalAppData,
    RoamingAppData,
    LocalLow,
    UserTemp,
    StoreAppData,
    UserPrograms,
    Desktop,
    Documents,
    Downloads,
    Pictures,
    Videos,
    Music,
    OneDrive,
    RecycleBin,
    SystemVolumeInformation,
    Recovery,
    PerfLogs,
    PageFile,
    HibernationFile,
    SwapFile,
}

public enum IdentitySource
{
    /// <summary>A folder Windows itself defines.</summary>
    System,
    /// <summary>Data belonging to an installed app, identified by its folder name.</summary>
    App,
    /// <summary>A file, described by its type.</summary>
    FileType,
}

/// <summary>What an item is, and where that answer came from. Nothing here changes a safety label or offers an action.</summary>
public sealed record FolderIdentity(IdentitySource Source, KnownFolder? Folder = null, string? AppName = null, string? TypeName = null);

/// <summary>Looks up installed apps, so a folder named after one can be explained without guessing.</summary>
public interface IInstalledApps
{
    /// <summary>The display name of an installed program or publisher matching a folder name, or <c>null</c>.</summary>
    string? NameForFolder(string folderName);

    /// <summary>The display name of a Store app from its package family name (e.g. <c>Microsoft.WindowsTerminal_8wekyb3d8bbwe</c>).</summary>
    string? NameForPackage(string packageFamilyName);
}

/// <summary>Describes file types the way Explorer does ("Text Document").</summary>
public interface IFileTypeNames
{
    string? Describe(string extension);
}

/// <summary>
/// Recognizes items from what the PC already knows: the places Windows defines, the apps that are installed, and file
/// types. Anything it doesn't recognize returns <c>null</c>.
/// </summary>
public sealed class FolderIdentifier
{
    private readonly Dictionary<string, KnownFolder> known;
    private readonly string[] appDataParents;
    private readonly string storeAppData;
    private readonly IInstalledApps apps;
    private readonly IFileTypeNames fileTypes;

    public FolderIdentifier(KnownLocations locations, IInstalledApps apps, IFileTypeNames fileTypes)
    {
        ArgumentNullException.ThrowIfNull(locations);
        this.apps = apps ?? throw new ArgumentNullException(nameof(apps));
        this.fileTypes = fileTypes ?? throw new ArgumentNullException(nameof(fileTypes));
        string home = locations.Home;
        string drive = locations.SystemDrive;
        known = new Dictionary<string, KnownFolder>(NodePath.Comparer)
        {
            [drive] = KnownFolder.SystemDrive,
            [locations.Windows] = KnownFolder.Windows,
            [NodePath.Join(locations.Windows, "System32")] = KnownFolder.System32,
            [NodePath.Join(locations.Windows, "SysWOW64")] = KnownFolder.SysWow64,
            [NodePath.Join(locations.Windows, "WinSxS")] = KnownFolder.WinSxS,
            [NodePath.Join(locations.Windows, "Installer")] = KnownFolder.WindowsInstaller,
            [NodePath.Join(locations.Windows, "SoftwareDistribution")] = KnownFolder.SoftwareDistribution,
            [NodePath.Join(locations.Windows, "Temp")] = KnownFolder.WindowsTemp,
            [locations.ProgramFiles] = KnownFolder.ProgramFiles,
            [locations.ProgramFilesX86] = KnownFolder.ProgramFilesX86,
            [locations.ProgramData] = KnownFolder.ProgramData,
            [locations.Users] = KnownFolder.Users,
            [NodePath.Join(locations.Users, "Public")] = KnownFolder.PublicProfile,
            [home] = KnownFolder.Home,
            [NodePath.Join(home, "AppData")] = KnownFolder.AppData,
            [locations.LocalAppData] = KnownFolder.LocalAppData,
            [locations.RoamingAppData] = KnownFolder.RoamingAppData,
            [NodePath.Join(NodePath.Join(home, "AppData"), "LocalLow")] = KnownFolder.LocalLow,
            [NodePath.Join(locations.LocalAppData, "Temp")] = KnownFolder.UserTemp,
            [NodePath.Join(locations.LocalAppData, "Packages")] = KnownFolder.StoreAppData,
            [NodePath.Join(locations.LocalAppData, "Programs")] = KnownFolder.UserPrograms,
            [NodePath.Join(home, "Desktop")] = KnownFolder.Desktop,
            [NodePath.Join(home, "Documents")] = KnownFolder.Documents,
            [NodePath.Join(home, "Downloads")] = KnownFolder.Downloads,
            [NodePath.Join(home, "Pictures")] = KnownFolder.Pictures,
            [NodePath.Join(home, "Videos")] = KnownFolder.Videos,
            [NodePath.Join(home, "Music")] = KnownFolder.Music,
            [NodePath.Join(home, "OneDrive")] = KnownFolder.OneDrive,
            [NodePath.Join(drive, "PerfLogs")] = KnownFolder.PerfLogs,
        };
        appDataParents =
        [
            locations.LocalAppData, locations.RoamingAppData, locations.ProgramData, locations.ProgramFiles,
            locations.ProgramFilesX86, NodePath.Join(NodePath.Join(home, "AppData"), "LocalLow"),
            NodePath.Join(locations.LocalAppData, "Programs"),
        ];
        storeAppData = NodePath.Join(locations.LocalAppData, "Packages");
    }

    public FolderIdentity? Identify(FileNode node, string path)
    {
        ArgumentNullException.ThrowIfNull(node);
        string trimmed = NodePath.Trim(path);
        if (known.TryGetValue(trimmed, out var folder)) return new FolderIdentity(IdentitySource.System, folder);
        if (AtDriveRoot(trimmed) is { } rootItem) return new FolderIdentity(IdentitySource.System, rootItem);
        if (node.Kind == NodeKind.File)
        {
            string extension = NodePath.Extension(node.Name);
            return extension.Length > 0 && fileTypes.Describe(extension) is { } type
                ? new FolderIdentity(IdentitySource.FileType, TypeName: type)
                : null;
        }
        return AppIdentity(trimmed);
    }

    private static KnownFolder? AtDriveRoot(string path)
    {
        if (NodePath.IsDriveRoot(path)) return KnownFolder.OtherDrive;
        string? parent = NodePath.Parent(path);
        if (parent is null || !NodePath.IsDriveRoot(parent)) return null;
        return NodePath.Name(path).ToUpperInvariant() switch
        {
            "$RECYCLE.BIN" => KnownFolder.RecycleBin,
            "SYSTEM VOLUME INFORMATION" => KnownFolder.SystemVolumeInformation,
            "RECOVERY" => KnownFolder.Recovery,
            "PAGEFILE.SYS" => KnownFolder.PageFile,
            "HIBERFIL.SYS" => KnownFolder.HibernationFile,
            "SWAPFILE.SYS" => KnownFolder.SwapFile,
            _ => null,
        };
    }

    /// <summary>Folders named after an app or its publisher: the support, cache and install folders that fill a PC.</summary>
    private FolderIdentity? AppIdentity(string path)
    {
        string? parent = NodePath.Parent(path);
        if (parent is null) return null;
        string name = NodePath.Name(path);
        if (NodePath.Comparer.Equals(parent, storeAppData))
        {
            return apps.NameForPackage(name) is { } storeApp ? new FolderIdentity(IdentitySource.App, AppName: storeApp) : null;
        }
        if (!appDataParents.Any(folder => NodePath.Comparer.Equals(folder, parent))) return null;
        return apps.NameForFolder(name) is { } appName ? new FolderIdentity(IdentitySource.App, AppName: appName) : null;
    }
}
