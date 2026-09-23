using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Temizlikci.Services.Scanning.Native;

namespace Temizlikci.Services.Scanning.Mft;

/// <summary>Why the Master File Table couldn't be read; the scanner then walks directories instead.</summary>
internal sealed class MftUnavailableException : Exception
{
    public MftUnavailableException(string message)
        : base(message)
    {
    }

    public MftUnavailableException()
    {
    }

    public MftUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reads an NTFS volume's Master File Table straight from the disk: the volume's geometry, the $MFT's own extents from
/// record 0, then every record in large sequential reads. Needs administrator rights; reads only.
/// </summary>
internal sealed partial class MftVolumeReader : IDisposable
{
    private const uint FsctlGetNtfsVolumeData = 0x00090064;
    private const int ChunkSize = 4 * 1024 * 1024;

    private readonly SafeFileHandle volume;
    private readonly NtfsVolumeData data;

    private MftVolumeReader(SafeFileHandle volume, NtfsVolumeData data)
    {
        this.volume = volume;
        this.data = data;
    }

    public int RecordSize => (int)data.BytesPerFileRecordSegment;

    /// <summary>Raw volume reads must start and end on sector boundaries (4 KB on Advanced Format disks).</summary>
    private int Alignment => (int)Math.Max(data.BytesPerFileRecordSegment, data.BytesPerSector);

    public long RecordCount => data.MftValidDataLength / data.BytesPerFileRecordSegment;

    public long BytesPerCluster => data.BytesPerCluster;

    /// <param name="volumeRoot">The volume's root, e.g. <c>C:\</c>.</param>
    /// <exception cref="MftUnavailableException">The volume can't be opened (no administrator rights) or isn't NTFS.</exception>
    public static MftVolumeReader Open(string volumeRoot)
    {
        string device = @"\\.\" + volumeRoot.TrimEnd('\\');
        var handle = NativeMethods.CreateFile(device, NativeMethods.GenericRead, NativeMethods.FileShareAll, 0, NativeMethods.OpenExisting, 0, 0);
        if (handle.IsInvalid) throw new MftUnavailableException($"Opening {device} failed with error {Marshal.GetLastPInvokeError()}.");
        if (!DeviceIoControl(handle, FsctlGetNtfsVolumeData, 0, 0, out NtfsVolumeData volumeData, (uint)Marshal.SizeOf<NtfsVolumeData>(), out _, 0))
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new MftUnavailableException($"{device} is not an NTFS volume (error {error}).");
        }
        if (volumeData.BytesPerFileRecordSegment is < 1024 or > 65536 || volumeData.BytesPerCluster == 0)
        {
            handle.Dispose();
            throw new MftUnavailableException($"{device} reports an unexpected record size {volumeData.BytesPerFileRecordSegment}.");
        }
        return new MftVolumeReader(handle, volumeData);
    }

    /// <summary>The $MFT's extents on disk, in bytes: (volume offset, length).</summary>
    public List<(long Offset, long Length)> Extents()
    {
        var block = new byte[Alignment];
        ReadExactly(block, data.MftStartLcn * data.BytesPerCluster);
        var first = block.AsSpan(0, RecordSize);
        if (!MftRecordParser.ApplyFixups(first)) throw new MftUnavailableException("The $MFT record is damaged.");
        var runs = MftRecordParser.DataRuns(first) ?? throw new MftUnavailableException("The $MFT record has no data runs.");
        var extents = new List<(long, long)>();
        foreach (var (lcn, clusters) in runs)
        {
            if (lcn < 0) throw new MftUnavailableException("The $MFT has a sparse run.");
            extents.Add((lcn * data.BytesPerCluster, clusters * data.BytesPerCluster));
        }
        // A heavily fragmented $MFT keeps the rest of its runs in extension records; reading only these would miss records.
        if (extents.Sum(extent => extent.Item2) < data.MftValidDataLength)
        {
            throw new MftUnavailableException("The $MFT's extents continue in an attribute list.");
        }
        return extents;
    }

    /// <summary>
    /// Calls <paramref name="visit"/> with every record (fixups applied) in record-number order, and <paramref name="progress"/>
    /// with the fraction read so far.
    /// </summary>
    public void ReadAll(List<(long Offset, long Length)> extents, RecordVisitor visit, Action<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extents);
        ArgumentNullException.ThrowIfNull(visit);
        long total = data.MftValidDataLength;
        long logical = 0;
        var buffer = new byte[ChunkSize];
        foreach (var (offset, length) in extents)
        {
            long done = 0;
            while (done < length && logical < total)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(Math.Min(ChunkSize, length - done), total - logical);
                count -= count % Alignment;
                if (count <= 0) break;
                ReadExactly(buffer.AsSpan(0, count), offset + done);
                for (int position = 0; position + RecordSize <= count; position += RecordSize)
                {
                    long recordNumber = (logical + position) / RecordSize;
                    var record = buffer.AsSpan(position, RecordSize);
                    if (MftRecordParser.ApplyFixups(record)) visit((int)recordNumber, record);
                }
                done += count;
                logical += count;
                progress(Math.Min(1, logical / (double)total));
            }
        }
    }

    /// <summary>Reads single records again, for the few names the first pass didn't keep.</summary>
    public void ReadRecords(List<(long Offset, long Length)> extents, IEnumerable<int> records, RecordVisitor visit)
    {
        ArgumentNullException.ThrowIfNull(extents);
        ArgumentNullException.ThrowIfNull(records);
        var block = new byte[Alignment];
        foreach (int record in records)
        {
            long logical = (long)record * RecordSize;
            long? offset = null;
            foreach (var (start, length) in extents)
            {
                if (logical < length)
                {
                    offset = start + logical;
                    break;
                }
                logical -= length;
            }
            if (offset is null) continue;
            long aligned = offset.Value - offset.Value % Alignment;
            ReadExactly(block, aligned);
            var buffer = block.AsSpan((int)(offset.Value - aligned), RecordSize);
            if (MftRecordParser.ApplyFixups(buffer)) visit(record, buffer);
        }
    }

    public delegate void RecordVisitor(int recordNumber, Span<byte> record);

    private void ReadExactly(Span<byte> destination, long offset)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int count = RandomAccess.Read(volume, destination[read..], offset + read);
            if (count <= 0) throw new MftUnavailableException($"Reading the volume stopped at offset {offset + read}.");
            read += count;
        }
    }

    public void Dispose() => volume.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct NtfsVolumeData
    {
        public long VolumeSerialNumber;
        public long NumberSectors;
        public long TotalClusters;
        public long FreeClusters;
        public long TotalReserved;
        public uint BytesPerSector;
        public uint BytesPerCluster;
        public uint BytesPerFileRecordSegment;
        public uint ClustersPerFileRecordSegment;
        public long MftValidDataLength;
        public long MftStartLcn;
        public long Mft2StartLcn;
        public long MftZoneStart;
        public long MftZoneEnd;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, nint inBuffer, uint inSize,
        out NtfsVolumeData outBuffer, uint outSize, out uint returned, nint overlapped);
}
