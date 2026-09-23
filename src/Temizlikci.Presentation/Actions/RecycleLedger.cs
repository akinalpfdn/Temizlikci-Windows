using CommunityToolkit.Mvvm.ComponentModel;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Tree;

namespace Temizlikci.Presentation.Actions;

/// <summary>An item this app moved to the Recycle Bin, with what's needed to put it back.</summary>
/// <param name="LocationPath">The scan location it came from.</param>
/// <param name="ParentIdPath">IDs of its folders from that scan's root to its parent, for putting it back into the tree.</param>
public sealed record RecycleRecord(Guid Id, FileNode Node, RecycledItem Item, DateTime DateUtc, string LocationPath, IReadOnlyList<string> ParentIdPath)
{
    public string Name => Node.Name;
    public string OriginalPath => Item.OriginalPath;
}

/// <summary>Items moved to the Recycle Bin during this session, shared by every location and the Recycle Bin view.</summary>
public sealed partial class RecycleLedger : ObservableObject
{
    private readonly IRecycleBin bin;
    private List<RecycleRecord> records = [];

    public RecycleLedger(IRecycleBin bin)
    {
        this.bin = bin ?? throw new ArgumentNullException(nameof(bin));
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<RecycleRecord> Records => records;

    public long TotalSize => records.Sum(record => record.Node.AllocatedSize);

    public void Add(RecycleRecord record)
    {
        records = [record, .. records];
        Changed();
    }

    public void Remove(RecycleRecord record)
    {
        records = records.Where(existing => existing.Id != record.Id).ToList();
        Changed();
    }

    public void OpenRecycleBin() => bin.Open();

    /// <summary>Drops records whose items are no longer in the Recycle Bin (emptied, or restored in Explorer). One
    /// lookup per record, off the UI thread; an unknown answer keeps the record.</summary>
    public async Task ReconcileAsync()
    {
        var checkedRecords = records;
        if (checkedRecords.Count == 0) return;
        var presence = await Task.Run(() => bin.Presence(checkedRecords.Select(record => record.Item).ToList())).ConfigureAwait(true);
        var gone = checkedRecords.Where((record, index) => presence[index] == RecyclePresence.Gone).Select(record => record.Id).ToHashSet();
        if (gone.Count == 0) return;
        records = records.Where(record => !gone.Contains(record.Id)).ToList();
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(TotalSize));
    }
}
