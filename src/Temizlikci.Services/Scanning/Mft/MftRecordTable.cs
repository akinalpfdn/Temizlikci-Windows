namespace Temizlikci.Services.Scanning.Mft;

/// <summary>
/// Everything the tree needs from the Master File Table, one slot per record number, in flat arrays: millions of
/// records must fit in tens of megabytes. Names are kept only for folders and for files large enough to be shown on
/// their own; the reader fetches the few it missed afterwards.
/// </summary>
internal sealed class MftRecordTable
{
    public const byte InUse = 0x01;
    public const byte Directory = 0x02;
    public const byte NameSurrogate = 0x04;
    public const byte Named = 0x08;
    public const int NoParent = -1;

    public MftRecordTable(int capacity)
    {
        Capacity = capacity;
        Flags = new byte[capacity];
        Parents = new int[capacity];
        Allocated = new long[capacity];
        Modified = new long[capacity];
        Names = new string?[capacity];
        Array.Fill(Parents, NoParent);
    }

    public int Capacity { get; }

    public byte[] Flags { get; }

    /// <summary>The record number of the folder holding the item's primary name.</summary>
    public int[] Parents { get; }

    public long[] Allocated { get; }

    /// <summary>Last write time as a FILETIME, 0 when unknown.</summary>
    public long[] Modified { get; }

    public string?[] Names { get; }

    public bool Has(int record, byte flag) => (Flags[record] & flag) != 0;

    /// <summary>Folds one parsed record in. Extension records add their streams and names to their base record.</summary>
    public void Apply(int recordNumber, in MftRecord record, long individualFileThreshold)
    {
        if (!record.InUse || recordNumber < 0 || recordNumber >= Capacity) return;
        int owner = record.BaseRecord != 0 && record.BaseRecord < Capacity ? (int)record.BaseRecord : recordNumber;
        if (owner == recordNumber)
        {
            Flags[owner] |= InUse;
            if (record.IsDirectory) Flags[owner] |= Directory;
            if (record.ModifiedFileTime > 0) Modified[owner] = record.ModifiedFileTime;
        }
        if (record.IsNameSurrogate) Flags[owner] |= NameSurrogate;
        Allocated[owner] += record.DataAllocated;
        if (record.HasName && !Has(owner, Named) && record.ParentRecord < Capacity)
        {
            Flags[owner] |= Named;
            Parents[owner] = (int)record.ParentRecord;
            bool keepName = record.IsDirectory || owner != recordNumber || Allocated[owner] >= individualFileThreshold;
            if (keepName) Names[owner] = new string(record.Name);
        }
    }

    /// <summary>Records whose name is needed but wasn't kept: files that only became large through an extension record.</summary>
    public IEnumerable<int> MissingNames(long individualFileThreshold)
    {
        for (int record = 0; record < Capacity; record++)
        {
            if (Has(record, Named) && Names[record] is null && !Has(record, Directory) && Allocated[record] >= individualFileThreshold)
            {
                yield return record;
            }
        }
    }
}
