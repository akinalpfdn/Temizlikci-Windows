using System.Buffers.Binary;

namespace Temizlikci.Services.Scanning.Mft;

/// <summary>What one FILE record says about a file or folder. Spans point into the record buffer.</summary>
internal ref struct MftRecord
{
    public bool InUse;
    public bool IsDirectory;
    /// <summary>Non-zero for an extension record: its attributes belong to this base record.</summary>
    public long BaseRecord;
    /// <summary>Allocated clusters of every non-resident data stream segment in this record, in bytes.</summary>
    public long DataAllocated;
    /// <summary>Last write time from $STANDARD_INFORMATION (FILETIME), 0 when absent.</summary>
    public long ModifiedFileTime;
    public bool HasName;
    /// <summary>Record number of the folder holding the primary (non-DOS) name.</summary>
    public long ParentRecord;
    public ReadOnlySpan<char> Name;
    /// <summary>The name is only a DOS 8.3 alias (PROFIL~1); a Win32 name may still come in an extension record.</summary>
    public bool NameIsDos;
    /// <summary>Junctions, symbolic links and mount points: they stand for another path and are never shown.</summary>
    public bool IsNameSurrogate;
}

/// <summary>
/// Decodes NTFS FILE records ("FILE" signature, update sequence fixups, attribute list). Pure byte parsing with no I/O,
/// so it is tested on synthetic records. Layout per the NTFS on-disk format (FILE_RECORD_SEGMENT_HEADER,
/// ATTRIBUTE_RECORD_HEADER, FILE_NAME).
/// </summary>
internal static class MftRecordParser
{
    public const int SectorStride = 512;

    private const uint AttributeStandardInformation = 0x10;
    private const uint AttributeFileName = 0x30;
    private const uint AttributeData = 0x80;
    private const uint AttributeReparsePoint = 0xC0;
    private const uint AttributeEnd = 0xFFFFFFFF;
    private const ushort FlagInUse = 0x0001;
    private const ushort FlagDirectory = 0x0002;
    private const byte NamespaceDos = 2;
    private const uint ReparseTagNameSurrogate = 0x20000000;
    private const long RecordNumberMask = 0x0000FFFFFFFFFFFF;

    /// <summary>
    /// Applies the update sequence fixups in place: the last two bytes of every 512-byte stride hold a check value while
    /// on disk, and the real bytes live in the update sequence array. <c>false</c> when the record is torn or not a FILE record.
    /// </summary>
    public static bool ApplyFixups(Span<byte> record)
    {
        if (record.Length < 48 || !record[..4].SequenceEqual("FILE"u8)) return false;
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        if (count < 1 || offset + count * 2 > record.Length || (count - 1) * SectorStride > record.Length) return false;
        ushort check = BinaryPrimitives.ReadUInt16LittleEndian(record[offset..]);
        for (int stride = 1; stride < count; stride++)
        {
            int end = stride * SectorStride - 2;
            if (BinaryPrimitives.ReadUInt16LittleEndian(record[end..]) != check) return false;
            record[(offset + stride * 2)..(offset + stride * 2 + 2)].CopyTo(record[end..]);
        }
        return true;
    }

    /// <summary>Reads a record whose fixups were applied. <c>false</c> for anything malformed.</summary>
    public static bool TryParse(ReadOnlySpan<byte> record, long bytesPerCluster, out MftRecord result)
    {
        result = default;
        if (record.Length < 48 || !record[..4].SequenceEqual("FILE"u8)) return false;
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record[22..]);
        result.InUse = (flags & FlagInUse) != 0;
        result.IsDirectory = (flags & FlagDirectory) != 0;
        result.BaseRecord = (long)BinaryPrimitives.ReadUInt64LittleEndian(record[32..]) & RecordNumberMask;
        if (!result.InUse) return true;

        int position = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
        int bestNamespaceRank = int.MaxValue;
        while (position + 16 <= record.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record[position..]);
            if (type == AttributeEnd) break;
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(position + 4)..]);
            if (length < 16 || position + length > record.Length) return false;
            var attribute = record.Slice(position, length);
            bool nonResident = attribute[8] != 0;
            switch (type)
            {
                case AttributeStandardInformation when !nonResident:
                    if (Resident(attribute) is { Length: >= 16 } standard)
                    {
                        result.ModifiedFileTime = BinaryPrimitives.ReadInt64LittleEndian(standard[8..]);
                    }
                    break;
                case AttributeFileName when !nonResident:
                    if (Resident(attribute) is { Length: >= 66 } fileName)
                    {
                        byte space = fileName[65];
                        int rank = NamespaceRank(space);
                        int characters = fileName[64];
                        if (rank < bestNamespaceRank && 66 + characters * 2 <= fileName.Length)
                        {
                            bestNamespaceRank = rank;
                            result.HasName = true;
                            result.ParentRecord = (long)BinaryPrimitives.ReadUInt64LittleEndian(fileName) & RecordNumberMask;
                            result.Name = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, char>(fileName.Slice(66, characters * 2));
                            result.NameIsDos = space == NamespaceDos;
                        }
                    }
                    break;
                case AttributeData when nonResident:
                    result.DataAllocated += NonResidentAllocated(attribute, bytesPerCluster);
                    break;
                case AttributeReparsePoint when !nonResident:
                    if (Resident(attribute) is { Length: >= 4 } reparse)
                    {
                        uint tag = BinaryPrimitives.ReadUInt32LittleEndian(reparse);
                        result.IsNameSurrogate = (tag & ReparseTagNameSurrogate) != 0;
                    }
                    break;
            }
            position += length;
        }
        return true;
    }

    /// <summary>Win32 and POSIX names are preferred; a DOS 8.3 alias is used only when it is the only name.</summary>
    private static int NamespaceRank(byte space) => space == NamespaceDos ? 1 : 0;

    private static ReadOnlySpan<byte> Resident(ReadOnlySpan<byte> attribute)
    {
        if (attribute.Length < 24) return default;
        int valueLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(attribute[16..]);
        int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[20..]);
        if (valueOffset + valueLength > attribute.Length) return default;
        return attribute.Slice(valueOffset, valueLength);
    }

    /// <summary>
    /// Clusters a non-resident stream segment really occupies: the non-sparse runs of its mapping pairs. The header's
    /// allocated-size fields aren't trusted — $BadClus:$Bad claims the whole volume without being flagged sparse — and
    /// counting runs per segment also covers files whose extents continue in extension records.
    /// </summary>
    private static long NonResidentAllocated(ReadOnlySpan<byte> attribute, long bytesPerCluster)
    {
        if (attribute.Length < 64) return 0;
        int runOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[32..]);
        if (runOffset <= 0 || runOffset >= attribute.Length) return 0;
        long clusters = 0;
        foreach (var (lcn, count) in DecodeRuns(attribute[runOffset..]))
        {
            if (lcn >= 0) clusters += count;
        }
        return clusters * bytesPerCluster;
    }

    /// <summary>The runs of the unnamed $DATA attribute of a record (used for $MFT's own extents), or <c>null</c>.</summary>
    public static List<(long Lcn, long Clusters)>? DataRuns(ReadOnlySpan<byte> record)
    {
        int position = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
        while (position + 16 <= record.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record[position..]);
            if (type == AttributeEnd) return null;
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(position + 4)..]);
            if (length < 16 || position + length > record.Length) return null;
            var attribute = record.Slice(position, length);
            if (type == AttributeData && attribute[8] != 0 && attribute[9] == 0)
            {
                int runOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[32..]);
                return DecodeRuns(attribute[runOffset..]);
            }
            position += length;
        }
        return null;
    }

    /// <summary>Decodes a mapping-pairs array: each run is a header byte (low nibble: length size, high nibble: offset
    /// size), a length and a signed offset relative to the previous run. A missing offset is a sparse run (LCN −1).</summary>
    public static List<(long Lcn, long Clusters)> DecodeRuns(ReadOnlySpan<byte> runs)
    {
        var result = new List<(long, long)>();
        long lcn = 0;
        int position = 0;
        while (position < runs.Length && runs[position] != 0)
        {
            int lengthSize = runs[position] & 0x0F;
            int offsetSize = runs[position] >> 4;
            position++;
            if (lengthSize == 0 || lengthSize > 8 || offsetSize > 8 || position + lengthSize + offsetSize > runs.Length) break;
            long clusters = ReadSigned(runs.Slice(position, lengthSize), signed: false);
            position += lengthSize;
            if (offsetSize == 0)
            {
                result.Add((-1, clusters));
                continue;
            }
            lcn += ReadSigned(runs.Slice(position, offsetSize), signed: true);
            position += offsetSize;
            result.Add((lcn, clusters));
        }
        return result;
    }

    private static long ReadSigned(ReadOnlySpan<byte> bytes, bool signed)
    {
        long value = 0;
        for (int index = bytes.Length - 1; index >= 0; index--) value = (value << 8) | bytes[index];
        if (signed && bytes.Length < 8 && (bytes[^1] & 0x80) != 0) value -= 1L << (bytes.Length * 8);
        return value;
    }
}
