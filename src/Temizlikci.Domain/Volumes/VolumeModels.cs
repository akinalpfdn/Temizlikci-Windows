namespace Temizlikci.Domain.Volumes;

/// <summary>
/// Capacity figures for the volume that holds a location. "Used" minus a whole-volume scan's total is the space no
/// scanned folder accounts for (NTFS metadata, shadow copies, folders that couldn't be read).
/// </summary>
public sealed record VolumeUsage(long TotalCapacity, long AvailableCapacity)
{
    public long UsedCapacity => TotalCapacity - AvailableCapacity;

    /// <summary>Used space that a scan of the whole volume did not attribute to any folder.</summary>
    public long Unattributed(long scannedSize) => Math.Max(0, UsedCapacity - scannedSize);
}

/// <summary>A mounted volume as the person knows it: "Local Disk (C:)".</summary>
/// <param name="RootPath">The root with a trailing separator, e.g. <c>C:\</c>.</param>
/// <param name="Label">The volume label; empty when it has none.</param>
/// <param name="FileSystem">NTFS, ReFS, FAT32…</param>
/// <param name="IsFixed">True for internal disks; removable and network drives are not offered as locations.</param>
/// <param name="IsSystem">True for the drive Windows runs from.</param>
public sealed record VolumeDescription(string RootPath, string Label, string FileSystem, bool IsFixed, bool IsSystem)
{
    /// <summary>"C:" — the letter and colon, without the separator.</summary>
    public string Letter => RootPath.TrimEnd('\\', '/');
}

/// <summary>Read-only facts about mounted volumes.</summary>
public interface IVolumeInfoProvider
{
    /// <summary>Fixed volumes in drive-letter order, the system drive first.</summary>
    IReadOnlyList<VolumeDescription> FixedVolumes();

    /// <summary>The volume that holds <paramref name="path"/>, or <c>null</c> when it can't be read.</summary>
    VolumeDescription? VolumeContaining(string path);

    /// <exception cref="IOException">The volume can't be queried.</exception>
    VolumeUsage Usage(string path);
}
