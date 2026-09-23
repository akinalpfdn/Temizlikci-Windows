using System.Buffers.Binary;
using System.Text;
using Temizlikci.Domain.Tree;

namespace Temizlikci.Domain.History;

/// <summary>
/// Encodes and decodes a whole scan tree. The tree is written in pre-order with only each node's name (paths are
/// rebuilt from the parent on the way back in), and numbers are fixed-width little-endian, so decoding is plain reads.
/// </summary>
public static class ScanArchive
{
    private static ReadOnlySpan<byte> Magic => "TMZW"u8;

    public const uint Version = 1;

    public sealed record Archived(FileNode Root, DateTime ScannedAtUtc, string LocationPath);

    public enum Failure
    {
        NotAnArchive,
        UnsupportedVersion,
        Truncated,
    }

    public sealed class ArchiveException : Exception
    {
        public ArchiveException(Failure failure, uint version = 0)
            : base($"{failure} {version}")
        {
            Reason = failure;
            FoundVersion = version;
        }

        public ArchiveException()
        {
        }

        public ArchiveException(string message)
            : base(message)
        {
        }

        public ArchiveException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public Failure Reason { get; }

        public uint FoundVersion { get; }
    }

    // MARK: Encoding

    public static byte[] Encode(FileNode root, DateTime scannedAtUtc, string locationPath)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(locationPath);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            WriteString(writer, locationPath);
            writer.Write(scannedAtUtc.ToUniversalTime().Ticks);
            Write(writer, root);
        }
        return stream.ToArray();
    }

    private static void Write(BinaryWriter writer, FileNode node)
    {
        writer.Write((byte)node.Kind);
        // Aggregates stand for their folder and have no name of their own.
        WriteString(writer, node.StandsForFolder ? string.Empty : node.Name);
        writer.Write(node.AllocatedSize);
        writer.Write(node.FileCount);
        writer.Write(node.ModifiedUtc?.Ticks ?? 0L);
        writer.Write(node.Children.Count);
        foreach (var child in node.Children) Write(writer, child);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    // MARK: Decoding

    /// <exception cref="ArchiveException">The data isn't an archive of this version, or is cut short.</exception>
    public static Archived Decode(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data);
        if (data.Length < 4 || !data[..4].SequenceEqual(Magic)) throw new ArchiveException(Failure.NotAnArchive);
        reader.Skip(4);
        uint version = reader.UInt32();
        if (version != Version) throw new ArchiveException(Failure.UnsupportedVersion, version);
        string location = reader.String();
        var scannedAt = new DateTime(reader.Int64(), DateTimeKind.Utc);
        var root = Read(ref reader);
        return new Archived(root, scannedAt, location);
    }

    private static FileNode Read(ref Reader reader)
    {
        byte tag = reader.Byte();
        if (tag > (byte)NodeKind.Pending) throw new ArchiveException(Failure.Truncated);
        var kind = (NodeKind)tag;
        string name = reader.String();
        long size = reader.Int64();
        int fileCount = reader.Int32();
        long ticks = reader.Int64();
        int childCount = reader.Int32();
        if (childCount < 0) throw new ArchiveException(Failure.Truncated);
        var children = new FileNode[childCount];
        for (int index = 0; index < childCount; index++) children[index] = Read(ref reader);
        return FileNode.Restore(name, kind, size, fileCount, ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc), children);
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private int offset;

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || offset + count > data.Length) throw new ArchiveException(Failure.Truncated);
            var slice = data.Slice(offset, count);
            offset += count;
            return slice;
        }

        public void Skip(int count) => Take(count);

        public byte Byte() => Take(1)[0];

        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

        public string String() => Encoding.UTF8.GetString(Take(Int32()));
    }
}

/// <summary>How old a saved scan may get before the app refreshes it by itself.</summary>
public enum RefreshPeriod
{
    Never = 0,
    Day = 1,
    ThreeDays = 3,
    Week = 7,
}

public static class RefreshPeriods
{
    public static IReadOnlyList<RefreshPeriod> All { get; } = [RefreshPeriod.Day, RefreshPeriod.ThreeDays, RefreshPeriod.Week, RefreshPeriod.Never];

    /// <summary>The age after which a refresh starts, or <c>null</c> when the app should never refresh on its own.</summary>
    public static TimeSpan? Interval(this RefreshPeriod period) =>
        period == RefreshPeriod.Never ? null : TimeSpan.FromDays((int)period);
}

/// <summary>Keeps the last full scan of each location so the app can show it again without reading the disk.</summary>
public interface IScanCache
{
    void Save(FileNode root, DateTime scannedAtUtc, string locationPath);

    /// <summary>The saved scan, or <c>null</c> when there is none for this location.</summary>
    ScanArchive.Archived? Load(string locationPath);

    void Remove(string locationPath);
}
