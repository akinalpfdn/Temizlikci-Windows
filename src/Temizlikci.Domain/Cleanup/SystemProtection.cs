using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.Cleanup;

/// <summary>
/// Locations the app refuses to move to the Recycle Bin, whatever the rules say (DECISIONS 2026-09-23). macOS protects
/// its system with SIP; on Windows an elevated app could recycle System32, so the app keeps its own floor.
/// </summary>
public sealed class SystemProtection
{
    private static readonly string[] ProtectedAtDriveRoot =
    [
        "$Recycle.Bin", "System Volume Information", "Recovery", "Boot", "EFI", "Config.Msi",
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "BOOTNXT", "DumpStack.log.tmp",
    ];

    private readonly string[] protectedTrees;
    private readonly string users;

    public SystemProtection(KnownLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        protectedTrees =
        [
            NodePath.Trim(locations.Windows),
            NodePath.Trim(locations.ProgramFiles),
            NodePath.Trim(locations.ProgramFilesX86),
            NodePath.Trim(locations.ProgramData),
        ];
        users = NodePath.Trim(locations.Users);
    }

    /// <summary>True when nothing at or below <paramref name="path"/> may be recycled from the app.</summary>
    public bool IsProtected(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string trimmed = NodePath.Trim(path);
        if (NodePath.IsDriveRoot(trimmed)) return true;
        foreach (var tree in protectedTrees)
        {
            if (NodePath.IsSameOrWithin(trimmed, tree)) return true;
        }
        // The folder of all profiles, each profile itself, and the profile's AppData root hold everything else.
        if (NodePath.Comparer.Equals(trimmed, users)) return true;
        string? parent = NodePath.Parent(trimmed);
        if (parent is not null && NodePath.Comparer.Equals(parent, users)) return true;
        if (parent is not null && NodePath.Comparer.Equals(NodePath.Parent(parent), users)
            && string.Equals(NodePath.Name(trimmed), "AppData", StringComparison.OrdinalIgnoreCase)) return true;
        // Items at a drive root that Windows owns, and everything inside them.
        string? root = RootChild(trimmed);
        return root is not null && ProtectedAtDriveRoot.Contains(root, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The name of the drive-root item <paramref name="path"/> is in or is, e.g. "$Recycle.Bin".</summary>
    private static string? RootChild(string path)
    {
        if (path.Length < 4 || path[1] != ':' || path[2] != NodePath.Separator) return null;
        string rest = path[3..];
        int separator = rest.IndexOf(NodePath.Separator, StringComparison.Ordinal);
        return separator < 0 ? rest : rest[..separator];
    }
}
