using CommunityToolkit.Mvvm.ComponentModel;

namespace Temizlikci.Presentation.Actions;

/// <summary>
/// The window's Edit › Undo: undoable actions register how to reverse themselves. Only Move to Recycle Bin uses it, so
/// the stack stays small; there is no redo, as putting an item back is itself visible in the Recycle Bin view.
/// </summary>
public sealed partial class UndoHistory : ObservableObject
{
    private const int Limit = 50;
    private readonly List<(string Name, Action Undo)> entries = [];

    public bool CanUndo => entries.Count > 0;

    /// <summary>What Undo would reverse, for the menu item ("Undo Move to Recycle Bin").</summary>
    public string? ActionName => entries.Count > 0 ? entries[^1].Name : null;

    public void Register(string name, Action undo)
    {
        entries.Add((name, undo));
        if (entries.Count > Limit) entries.RemoveAt(0);
        Changed();
    }

    public void Undo()
    {
        if (entries.Count == 0) return;
        var (_, undo) = entries[^1];
        entries.RemoveAt(entries.Count - 1);
        Changed();
        undo();
    }

    /// <summary>Drops the entry for something that was already reversed another way (the toast's Undo, Put Back).</summary>
    public void Forget(Action undo)
    {
        int index = entries.FindLastIndex(entry => ReferenceEquals(entry.Undo, undo));
        if (index < 0) return;
        entries.RemoveAt(index);
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(ActionName));
    }
}
