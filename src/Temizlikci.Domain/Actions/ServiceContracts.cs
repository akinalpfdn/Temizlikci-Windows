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

/// <summary>A command-line tool to run: an executable and an argument list, never a shell command line.</summary>
public sealed record ToolCommand(string Executable, IReadOnlyList<string> Arguments)
{
    /// <summary>How the tool writes its output; <c>wsl.exe</c> writes UTF-16. <c>null</c> is the console's code page.</summary>
    public System.Text.Encoding? OutputEncoding { get; init; }
}

/// <summary>The outcome of a command-line tool.</summary>
public sealed record ToolOutput(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs a command-line tool off the UI thread, without a console window.</summary>
public interface IToolRunner
{
    /// <exception cref="System.ComponentModel.Win32Exception">The tool couldn't be started.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; the tool's process tree has been stopped.</exception>
    Task<ToolOutput> RunAsync(ToolCommand command, CancellationToken cancellationToken);
}

public enum ToolFailure
{
    /// <summary>The tool isn't installed (no WSL, no Android Studio).</summary>
    NotInstalled,
    /// <summary>The action needs the app to run as administrator.</summary>
    NeedsAdministrator,
    /// <summary>The tool ran and reported an error; <see cref="ToolException.Detail"/> has its words.</summary>
    Failed,
}

/// <summary>A tool action that didn't happen. The message shown to people comes from the presentation layer.</summary>
public sealed class ToolException : Exception
{
    public ToolException(ToolFailure failure, string tool, string? detail = null, Exception? innerException = null)
        : base($"{failure}: {tool}", innerException)
    {
        Failure = failure;
        Tool = tool;
        Detail = detail;
    }

    public ToolException()
    {
        Tool = string.Empty;
    }

    public ToolException(string message)
        : base(message)
    {
        Tool = string.Empty;
    }

    public ToolException(string message, Exception innerException)
        : base(message, innerException)
    {
        Tool = string.Empty;
    }

    public ToolFailure Failure { get; }

    /// <summary>The tool's name as people know it (<c>wsl</c>, <c>DISM</c>).</summary>
    public string Tool { get; }

    /// <summary>The tool's own last words, when it gave any.</summary>
    public string? Detail { get; }
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

    /// <summary>System Properties › System Protection, where restore points and their space are managed.</summary>
    void OpenSystemProtection();
}
