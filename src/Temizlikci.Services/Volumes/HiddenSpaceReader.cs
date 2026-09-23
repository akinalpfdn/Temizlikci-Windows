using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Temizlikci.Domain.Volumes;
using Temizlikci.Services.Scanning.Native;

namespace Temizlikci.Services.Volumes;

/// <summary>
/// Reads the parts of a volume's used space that no folder listing shows: the Master File Table's size
/// (FSCTL_GET_NTFS_VOLUME_DATA) and the space shadow copies take (WMI Win32_ShadowStorage). Both need administrator
/// rights; unelevated, the parts stay unknown and the breakdown shows them as part of the remainder.
/// </summary>
public sealed partial class HiddenSpaceReader : IHiddenSpaceReader
{
    private const uint FsctlGetNtfsVolumeData = 0x00090064;

    public Task<HiddenSpace> ReadAsync(string volumeRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volumeRoot);
        return Task.Run(() => new HiddenSpace(MasterFileTableSize(volumeRoot), ShadowCopySize(volumeRoot)), cancellationToken);
    }

    private static long? MasterFileTableSize(string volumeRoot)
    {
        using var volume = NativeMethods.CreateFile(@"\\.\" + volumeRoot.TrimEnd('\\'), NativeMethods.GenericRead, NativeMethods.FileShareAll, 0, NativeMethods.OpenExisting, 0, 0);
        if (volume.IsInvalid) return null;
        var data = new long[16];
        if (!DeviceIoControl(volume, FsctlGetNtfsVolumeData, 0, 0, data, (uint)(data.Length * sizeof(long)), out _, 0)) return null;
        // NTFS_VOLUME_DATA_BUFFER: MftValidDataLength follows five 8-byte and four 4-byte fields.
        return data[7];
    }

    private static long? ShadowCopySize(string volumeRoot)
    {
        var buffer = new char[64];
        if (!GetVolumeNameForVolumeMountPoint(volumeRoot.EndsWith('\\') ? volumeRoot : volumeRoot + "\\", buffer, (uint)buffer.Length)) return null;
        string volumeId = new string(buffer).TrimEnd('\0');
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UsedSpace, Volume FROM Win32_ShadowStorage");
            long total = 0;
            bool found = false;
            foreach (var storage in searcher.Get())
            {
                using (storage)
                {
                    string reference = storage["Volume"] as string ?? string.Empty;
                    // Volume is a WMI reference like Win32_Volume.DeviceID="\\\\?\\Volume{…}\\"; compare the GUID path.
                    if (!reference.Replace(@"\\", @"\", StringComparison.Ordinal).Contains(volumeId, StringComparison.OrdinalIgnoreCase)) continue;
                    total += Convert.ToInt64(storage["UsedSpace"], System.Globalization.CultureInfo.InvariantCulture);
                    found = true;
                }
            }
            return found ? total : 0;
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or COMException)
        {
            // Unelevated or WMI unavailable: the shadow copies stay inside the remainder.
            return null;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, nint inBuffer, uint inSize,
        [Out] long[] outBuffer, uint outSize, out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPoint(string mountPoint, [Out] char[] volumeName, uint length);
}
