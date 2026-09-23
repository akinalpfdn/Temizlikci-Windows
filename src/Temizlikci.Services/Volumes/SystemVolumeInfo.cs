using Temizlikci.Domain.Volumes;

namespace Temizlikci.Services.Volumes;

/// <summary>Volume facts from <see cref="DriveInfo"/>; nothing here needs elevation.</summary>
public sealed class SystemVolumeInfo : IVolumeInfoProvider
{
    private static readonly string SystemRoot =
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

    public IReadOnlyList<VolumeDescription> FixedVolumes() =>
        DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(Describe)
            .OrderByDescending(volume => volume.IsSystem)
            .ThenBy(volume => volume.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public VolumeDescription? VolumeContaining(string path)
    {
        string? root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root)) return null;
        var drive = new DriveInfo(root);
        return drive.IsReady ? Describe(drive) : null;
    }

    public VolumeUsage Usage(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new IOException($"No volume holds {path}.");
        var drive = new DriveInfo(root);
        // TotalFreeSpace, not AvailableFreeSpace: quotas are per user, but "used" means used by anyone.
        return new VolumeUsage(drive.TotalSize, drive.TotalFreeSpace);
    }

    private static VolumeDescription Describe(DriveInfo drive) => new(
        drive.RootDirectory.FullName,
        SafeLabel(drive),
        SafeFormat(drive),
        drive.DriveType == DriveType.Fixed,
        string.Equals(drive.RootDirectory.FullName, SystemRoot, StringComparison.OrdinalIgnoreCase));

    private static string SafeLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A label that can't be read isn't actionable; the sidebar falls back to "Local Disk".
            return string.Empty;
        }
    }

    private static string SafeFormat(DriveInfo drive)
    {
        try
        {
            return drive.DriveFormat;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unknown file system: the MFT scanner simply isn't offered for it.
            return string.Empty;
        }
    }
}
