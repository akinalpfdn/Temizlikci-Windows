namespace Temizlikci.Domain.Tools;

/// <summary>A WSL distribution as Windows registers it (HKCU\…\Lxss): where its files live and which WSL runs it.</summary>
/// <param name="BasePath">The folder holding the distribution's disk (WSL 2) or root file system (WSL 1).</param>
/// <param name="VhdFileName">The virtual disk's file name inside <paramref name="BasePath"/>; WSL 2 only.</param>
public sealed record WslRegistration(string Name, string BasePath, int Version, string VhdFileName, bool IsDefault);

/// <summary>A distribution with what the Developer view shows and what its actions need.</summary>
/// <param name="DiskSize">Size of the virtual disk on this drive; <c>null</c> for WSL 1 or when it can't be read.</param>
public sealed record WslDistribution(WslRegistration Registration, bool IsRunning, long? DiskSize)
{
    private const string DockerPrefix = "docker-desktop";

    public string Name => Registration.Name;

    public bool IsDefault => Registration.IsDefault;

    public int Version => Registration.Version;

    /// <summary>The distribution's virtual disk (<c>ext4.vhdx</c>); <c>null</c> for WSL 1, which has no disk to compact.</summary>
    public string? DiskPath => Version == 2 ? Path.Combine(WslOutput.PlainPath(Registration.BasePath), Registration.VhdFileName) : null;

    /// <summary>Docker Desktop's own distributions: removing them breaks Docker, which manages their data itself.</summary>
    public bool IsManagedByDocker => Name.StartsWith(DockerPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Docker's disks are left to Docker too: stopping its distribution to compact it would stop Docker.</summary>
    public bool CanCompact => DiskPath is not null && !IsManagedByDocker;

    public bool CanUnregister => !IsManagedByDocker;
}

/// <summary>Reading what <c>wsl.exe</c> prints.</summary>
public static class WslOutput
{
    /// <summary>
    /// Distribution names from <c>wsl --list --quiet</c> (optionally <c>--running</c>): one per line, in UTF-16, sometimes
    /// with a byte-order mark or stray NULs. Only names in <paramref name="known"/> count, so a localized message such as
    /// "There are no running distributions." is never mistaken for one.
    /// </summary>
    public static IReadOnlySet<string> Names(string output, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(output);
        var knownNames = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Replace("\0", string.Empty, StringComparison.Ordinal).Replace("\uFEFF", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            string name = line.Trim();
            if (knownNames.Contains(name)) names.Add(name);
        }
        return names;
    }

    /// <summary>The registry keeps paths in <c>\\?\C:\…</c> form; tools and people want <c>C:\…</c>.</summary>
    public static string PlainPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + path[8..];
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    /// <summary>The <c>diskpart /s</c> script that compacts a virtual disk: attached read-only, so its contents can't
    /// change. Script mode is what makes diskpart stop and return an error code when a step fails.</summary>
    public static string CompactScript(string diskPath)
    {
        ArgumentNullException.ThrowIfNull(diskPath);
        if (diskPath.Contains('"', StringComparison.Ordinal)) throw new ArgumentException("A disk path can't contain quotes.", nameof(diskPath));
        return string.Join("\r\n", $"select vdisk file=\"{diskPath}\"", "attach vdisk readonly", "compact vdisk", "detach vdisk", "exit") + "\r\n";
    }
}

/// <summary>WSL distributions: list them, give back unused disk space, remove them. Only through <c>wsl</c> and <c>diskpart</c>.</summary>
public interface IWslManager
{
    /// <exception cref="Actions.ToolException">WSL isn't installed or wouldn't answer.</exception>
    Task<IReadOnlyList<WslDistribution>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Stops the distribution and compacts its virtual disk. Needs administrator rights.</summary>
    /// <exception cref="Actions.ToolException">The disk couldn't be compacted.</exception>
    Task CompactAsync(WslDistribution distribution, CancellationToken cancellationToken);

    /// <summary>Removes the distribution and everything in it, through <c>wsl --unregister</c>.</summary>
    /// <exception cref="Actions.ToolException">WSL refused.</exception>
    Task UnregisterAsync(WslDistribution distribution, CancellationToken cancellationToken);
}

/// <summary>Windows' component store (WinSxS): DISM is the only safe way to shrink it.</summary>
public interface IComponentStore
{
    /// <summary>Removes superseded components (<c>DISM /Online /Cleanup-Image /StartComponentCleanup</c>). Needs administrator rights; takes minutes.</summary>
    /// <exception cref="Actions.ToolException">DISM reported an error.</exception>
    Task CleanUpAsync(CancellationToken cancellationToken);
}
