using System.Buffers.Binary;
using System.Text;

namespace Temizlikci.Tests.Services;

/// <summary>
/// Writes NTFS FILE records byte by byte, as they sit on disk (update sequence check values in place), so the MFT parser
/// is tested without administrator rights or a real volume.
/// </summary>
internal sealed class MftRecordBuilder
{
    public const int RecordSize = 1024;
    private const int UpdateSequenceOffset = 48;
    private const int FirstAttributeOffset = 56;
    private const ushort CheckValue = 0x0A0B;

    private readonly List<byte[]> attributes = [];
    private ushort flags = 0x0001;
    private long baseRecord;

    public MftRecordBuilder Directory()
    {
        flags |= 0x0002;
        return this;
    }

    public MftRecordBuilder Deleted()
    {
        flags = 0;
        return this;
    }

    public MftRecordBuilder ExtensionOf(long record)
    {
        baseRecord = record;
        return this;
    }

    public MftRecordBuilder StandardInformation(DateTime modifiedUtc)
    {
        var value = new byte[72];
        BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(0), modifiedUtc.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(8), modifiedUtc.ToFileTimeUtc());
        attributes.Add(Resident(0x10, value));
        return this;
    }

    /// <param name="space">0 POSIX, 1 Win32, 2 DOS, 3 Win32 and DOS.</param>
    public MftRecordBuilder FileName(string name, long parent, byte space = 1)
    {
        var chars = Encoding.Unicode.GetBytes(name);
        var value = new byte[66 + chars.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(value.AsSpan(0), (ulong)parent | (1UL << 48));
        value[64] = (byte)name.Length;
        value[65] = space;
        chars.CopyTo(value.AsSpan(66));
        attributes.Add(Resident(0x30, value));
        return this;
    }

    public const int ClusterSize = 4096;

    /// <summary>A non-resident data segment of <paramref name="clusters"/> real clusters, optionally followed by sparse
    /// ones. <paramref name="claimedAllocated"/> fills the header's size fields, which the parser must not trust.</summary>
    public MftRecordBuilder Data(long clusters, long sparseClusters = 0, long startingVcn = 0, ushort attributeFlags = 0,
        string? streamName = null, long? claimedAllocated = null)
    {
        var runs = new List<byte>();
        if (clusters > 0) runs.AddRange(Run(clusters, lcn: 0x1000));
        if (sparseClusters > 0) runs.AddRange(Run(sparseClusters, lcn: null));
        runs.Add(0);
        byte[] name = streamName is null ? [] : Encoding.Unicode.GetBytes(streamName);
        const int headerLength = 72;
        int runOffset = Align(headerLength + name.Length);
        var attribute = new byte[Align(runOffset + runs.Count)];
        long claimed = claimedAllocated ?? (clusters + sparseClusters) * ClusterSize;
        BinaryPrimitives.WriteUInt32LittleEndian(attribute.AsSpan(0), 0x80);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute.AsSpan(4), (uint)attribute.Length);
        attribute[8] = 1;
        attribute[9] = (byte)(name.Length / 2);
        BinaryPrimitives.WriteUInt16LittleEndian(attribute.AsSpan(10), headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(attribute.AsSpan(12), attributeFlags);
        BinaryPrimitives.WriteInt64LittleEndian(attribute.AsSpan(16), startingVcn);
        BinaryPrimitives.WriteUInt16LittleEndian(attribute.AsSpan(32), (ushort)runOffset);
        BinaryPrimitives.WriteInt64LittleEndian(attribute.AsSpan(40), claimed);
        BinaryPrimitives.WriteInt64LittleEndian(attribute.AsSpan(48), claimed);
        BinaryPrimitives.WriteInt64LittleEndian(attribute.AsSpan(56), claimed);
        BinaryPrimitives.WriteInt64LittleEndian(attribute.AsSpan(64), claimed);
        name.CopyTo(attribute.AsSpan(headerLength));
        runs.ToArray().CopyTo(attribute.AsSpan(runOffset));
        attributes.Add(attribute);
        return this;
    }

    /// <summary>One mapping pair: minimal little-endian length, then (unless sparse) the LCN offset.</summary>
    private static IEnumerable<byte> Run(long clusters, long? lcn)
    {
        byte[] length = Minimal(clusters);
        byte[] offset = lcn is { } value ? Minimal(value) : [];
        return [(byte)(length.Length | (offset.Length << 4)), .. length, .. offset];
    }

    private static byte[] Minimal(long value)
    {
        var bytes = new List<byte>();
        do
        {
            bytes.Add((byte)value);
            value >>= 8;
        } while (value != 0 || (bytes[^1] & 0x80) != 0);
        return [.. bytes];
    }

    public MftRecordBuilder ResidentData(int length) => Add(Resident(0x80, new byte[length]));

    public MftRecordBuilder Reparse(uint tag)
    {
        var value = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(value, tag);
        return Add(Resident(0xC0, value));
    }

    private MftRecordBuilder Add(byte[] attribute)
    {
        attributes.Add(attribute);
        return this;
    }

    /// <summary>The record as stored on disk: the last two bytes of each 512-byte stride replaced by the check value.</summary>
    public byte[] Build()
    {
        var record = new byte[RecordSize];
        "FILE"u8.CopyTo(record);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), UpdateSequenceOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), RecordSize / 512 + 1);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(20), FirstAttributeOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(22), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(28), RecordSize);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(32), baseRecord == 0 ? 0 : (ulong)baseRecord | (1UL << 48));
        int position = FirstAttributeOffset;
        foreach (var attribute in attributes)
        {
            attribute.CopyTo(record.AsSpan(position));
            position += attribute.Length;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(position), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(24), (uint)(position + 8));

        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(UpdateSequenceOffset), CheckValue);
        for (int stride = 1; stride <= RecordSize / 512; stride++)
        {
            int end = stride * 512 - 2;
            record.AsSpan(end, 2).CopyTo(record.AsSpan(UpdateSequenceOffset + stride * 2));
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(end), CheckValue);
        }
        return record;
    }

    private static byte[] Resident(uint type, byte[] value)
    {
        var attribute = new byte[Align(24 + value.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(attribute.AsSpan(0), type);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute.AsSpan(4), (uint)attribute.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute.AsSpan(16), (uint)value.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(attribute.AsSpan(20), 24);
        value.CopyTo(attribute.AsSpan(24));
        return attribute;
    }

    private static int Align(int value) => (value + 7) & ~7;
}
