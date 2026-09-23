namespace Temizlikci.Domain.Actions;

/// <summary>Whether an item the app moved to the Recycle Bin is still there.</summary>
public enum RecyclePresence
{
    Present,
    /// <summary>Emptied from the Recycle Bin, or restored or deleted there in Explorer.</summary>
    Gone,
    /// <summary>Windows wouldn't say; treated as still there, so a missing permission never empties the list.</summary>
    Unknown,
}

/// <summary>An item in the Recycle Bin: where it came from and where Windows keeps it now (the <c>$R…</c> entry).</summary>
public sealed record RecycledItem(string OriginalPath, string RecycledPath);

public enum RecycleFailure
{
    NoPermission,
    Missing,
    InUse,
    /// <summary>The item is in a location the app never recycles (SystemProtection).</summary>
    Protected,
    Failed,
    PutBackFailed,
    /// <summary>Put Back found something else already at the original location.</summary>
    Occupied,
}

/// <summary>A Recycle Bin operation that didn't happen. The message shown to people comes from the presentation layer.</summary>
public sealed class RecycleException : Exception
{
    public RecycleException(RecycleFailure failure, string name, Exception? innerException = null)
        : base($"{failure}: {name}", innerException)
    {
        Failure = failure;
        Name = name;
    }

    public RecycleException()
    {
        Name = string.Empty;
    }

    public RecycleException(string message)
        : base(message)
    {
        Name = string.Empty;
    }

    public RecycleException(string message, Exception innerException)
        : base(message, innerException)
    {
        Name = string.Empty;
    }

    public RecycleFailure Failure { get; }

    /// <summary>The item's name, for the message.</summary>
    public string Name { get; }
}

/// <summary>Moves items to the Recycle Bin and back. The only way the app removes anything the person owns.</summary>
public interface IRecycleBin
{
    /// <exception cref="RecycleException">The item couldn't be recycled.</exception>
    RecycledItem MoveToRecycleBin(string path);

    /// <exception cref="RecycleException">The item couldn't be restored.</exception>
    void PutBack(RecycledItem item);

    /// <summary>Whether each recycled item is still in the Recycle Bin, in the same order.</summary>
    IReadOnlyList<RecyclePresence> Presence(IReadOnlyList<RecycledItem> items);

    void Open();
}

/// <summary>The outcome of a command-line tool.</summary>
public sealed record ToolOutput(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs a command-line tool with an argument list (never through a shell), off the UI thread.</summary>
public interface IToolRunner
{
    /// <param name="outputEncoding">How the tool writes its output; <c>wsl.exe</c> writes UTF-16.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The tool couldn't be started.</exception>
    Task<ToolOutput> RunAsync(string executable, IReadOnlyList<string> arguments, System.Text.Encoding? outputEncoding, CancellationToken cancellationToken);
}

/// <summary>Administrator rights for the whole app — the Windows counterpart of Full Disk Access.</summary>
public interface IElevation
{
    bool IsElevated { get; }

    /// <summary>Starts the app again as administrator (UAC asks). <c>false</c> when the person declined.</summary>
    bool RestartElevated();
}

/// <summary>Explorer and Settings: the places the app hands people over to.</summary>
public interface IShell
{
    void ShowInExplorer(string path);

    void ShowProperties(string path);

    void OpenUri(Uri uri);
}
