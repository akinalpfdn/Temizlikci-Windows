using System.Diagnostics;
using System.Runtime.InteropServices;
using Temizlikci.Domain.Actions;

namespace Temizlikci.Services.RecycleBin;

/// <summary>
/// Moves items to the Recycle Bin through the shell's own file operation (IFileOperation), the way Explorer does, and
/// puts them back. The only way the app removes anything the person owns.
/// </summary>
/// <remarks>
/// Flags: undo allowed, recycle on delete, no confirmation — but with the "nuke" warning, so an item too large for the
/// Recycle Bin is never destroyed without the shell asking first. The shell reports where it put the item (the
/// <c>$R…</c> entry), which is what Put Back moves back.
/// </remarks>
public sealed partial class ShellRecycleBin : IRecycleBin
{
    private const uint FofNoConfirmation = 0x0010;
    private const uint FofAllowUndo = 0x0040;
    private const uint FofNoErrorUi = 0x0400;
    private const uint FofWantNukeWarning = 0x4000;
    private const uint FofxRecycleOnDelete = 0x00080000;
    private const uint FofxEarlyFailure = 0x00100000;
    private const uint SigdnFileSystemPath = 0x80058000;
    private const int ErrorAccessDenied = unchecked((int)0x80070005);
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorFileNotFound = unchecked((int)0x80070002);
    private const int ErrorPathNotFound = unchecked((int)0x80070003);
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    private readonly Func<nint> ownerWindow;

    /// <param name="ownerWindow">The window the shell's own dialogs (the nuke warning) belong to.</param>
    public ShellRecycleBin(Func<nint> ownerWindow)
    {
        this.ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
    }

    public RecycledItem MoveToRecycleBin(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string name = Path.GetFileName(path.TrimEnd('\\'));
        if (!Path.Exists(path)) throw new RecycleException(RecycleFailure.Missing, name);

        var operation = (IFileOperation)new FileOperationClass();
        var sink = new DeleteSink();
        try
        {
            operation.SetOperationFlags(FofAllowUndo | FofxRecycleOnDelete | FofNoConfirmation | FofWantNukeWarning | FofNoErrorUi | FofxEarlyFailure);
            nint owner = ownerWindow();
            if (owner != 0) operation.SetOwnerWindow(owner);
            var item = CreateItem(path);
            operation.DeleteItem(item, sink);
            int result = operation.PerformOperations();
            operation.GetAnyOperationsAborted(out bool aborted);
            if (aborted || result == ErrorCancelled) throw new RecycleException(RecycleFailure.Failed, name);
            if (result < 0 || sink.Result < 0) throw Failure(sink.Result < 0 ? sink.Result : result, name);
            // Without a recycled item the shell destroyed it after the person agreed to its warning: nothing to put back.
            return new RecycledItem(path, sink.RecycledPath ?? string.Empty);
        }
        catch (COMException exception)
        {
            throw Failure(exception.HResult, name, exception);
        }
        finally
        {
            Marshal.FinalReleaseComObject(operation);
        }
    }

    /// <summary>Moves the <c>$R…</c> entry back and removes its <c>$I…</c> record, as Restore in Explorer does. Never
    /// replaces something that took the item's place in the meantime.</summary>
    public void PutBack(RecycledItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        string name = Path.GetFileName(item.OriginalPath.TrimEnd('\\'));
        if (item.RecycledPath.Length == 0 || !Path.Exists(item.RecycledPath)) throw new RecycleException(RecycleFailure.PutBackFailed, name);
        if (Path.Exists(item.OriginalPath)) throw new RecycleException(RecycleFailure.Occupied, name);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.OriginalPath)!);
            if (Directory.Exists(item.RecycledPath)) Directory.Move(item.RecycledPath, item.OriginalPath);
            else File.Move(item.RecycledPath, item.OriginalPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RecycleException(RecycleFailure.PutBackFailed, name, exception);
        }
        string information = InformationFile(item.RecycledPath);
        // The $I file is the Recycle Bin's own record of the restored item; left behind it would show a broken entry.
        if (File.Exists(information)) File.Delete(information);
    }

    public IReadOnlyList<RecyclePresence> Presence(IReadOnlyList<RecycledItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Select(item =>
        {
            if (item.RecycledPath.Length == 0) return RecyclePresence.Gone;
            try
            {
                return File.Exists(item.RecycledPath) || Directory.Exists(item.RecycledPath) ? RecyclePresence.Present : RecyclePresence.Gone;
            }
            catch (UnauthorizedAccessException)
            {
                return RecyclePresence.Unknown;
            }
        }).ToList();
    }

    public void Open()
    {
        using var _ = Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = false });
    }

    /// <summary><c>…\$RABC123.txt</c> → <c>…\$IABC123.txt</c>.</summary>
    internal static string InformationFile(string recycledPath)
    {
        string folder = Path.GetDirectoryName(recycledPath) ?? string.Empty;
        string name = Path.GetFileName(recycledPath);
        return name.StartsWith("$R", StringComparison.OrdinalIgnoreCase) ? Path.Join(folder, "$I" + name[2..]) : string.Empty;
    }

    private static RecycleException Failure(int result, string name, Exception? inner = null) => result switch
    {
        ErrorAccessDenied => new RecycleException(RecycleFailure.NoPermission, name, inner),
        ErrorSharingViolation => new RecycleException(RecycleFailure.InUse, name, inner),
        ErrorFileNotFound or ErrorPathNotFound => new RecycleException(RecycleFailure.Missing, name, inner),
        _ => new RecycleException(RecycleFailure.Failed, name, inner),
    };

    private static IShellItem CreateItem(string path)
    {
        var id = typeof(IShellItem).GUID;
        int result = SHCreateItemFromParsingName(path, 0, ref id, out var item);
        if (result < 0) throw Failure(result, Path.GetFileName(path));
        return item;
    }

    /// <summary>Collects where the shell put the item and how the delete went.</summary>
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class DeleteSink : IFileOperationProgressSink
    {
        public string? RecycledPath { get; private set; }

        public int Result { get; private set; }

        public void PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? newlyCreated)
        {
            Result = result;
            if (newlyCreated is null || result < 0) return;
            newlyCreated.GetDisplayName(SigdnFileSystemPath, out nint name);
            try
            {
                RecycledPath = Marshal.PtrToStringUni(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }

        public void StartOperations() { }
        public void FinishOperations(int result) { }
        public void PreRenameItem(uint flags, IShellItem item, string newName) { }
        public void PostRenameItem(uint flags, IShellItem item, string newName, int result, IShellItem newlyCreated) { }
        public void PreMoveItem(uint flags, IShellItem item, IShellItem destination, string newName) { }
        public void PostMoveItem(uint flags, IShellItem item, IShellItem destination, string newName, int result, IShellItem newlyCreated) { }
        public void PreCopyItem(uint flags, IShellItem item, IShellItem destination, string newName) { }
        public void PostCopyItem(uint flags, IShellItem item, IShellItem destination, string newName, int result, IShellItem newlyCreated) { }
        public void PreDeleteItem(uint flags, IShellItem item) { }
        public void PreNewItem(uint flags, IShellItem destination, string newName) { }
        public void PostNewItem(uint flags, IShellItem destination, string newName, string templateName, uint attributes, int result, IShellItem newItem) { }
        public void UpdateProgress(uint workTotal, uint workSoFar) { }
        public void ResetTimer() { }
        public void PauseTimer() { }
        public void ResumeTimer() { }
    }

    [ComImport]
    [Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    private class FileOperationClass
    {
    }

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        uint Advise(IFileOperationProgressSink sink);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink? sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        uint NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IFileOperationProgressSink? sink);
        [PreserveSig]
        int PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport]
    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        void StartOperations();
        void FinishOperations(int result);
        void PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, int result, IShellItem newlyCreated);
        void PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, int result, IShellItem newlyCreated);
        void PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, int result, IShellItem newlyCreated);
        void PreDeleteItem(uint flags, IShellItem item);
        void PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? newlyCreated);
        void PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, [MarshalAs(UnmanagedType.LPWStr)] string templateName, uint attributes, int result, IShellItem newItem);
        void UpdateProgress(uint workTotal, uint workSoFar);
        void ResetTimer();
        void PauseTimer();
        void ResumeTimer();
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, ref Guid handler, ref Guid interfaceId, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint form, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

#pragma warning disable SYSLIB1054 // Returns a COM interface; the built-in COM interop marshals it, LibraryImport can't.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid interfaceId, out IShellItem item);
#pragma warning restore SYSLIB1054
}
