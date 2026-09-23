using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Temizlikci.Tests.Support;

/// <summary>
/// A throwaway folder under %TEMP% for tests that need a real file system. Never points at the developer's own files;
/// removes itself (after undoing any permission changes) when disposed.
/// </summary>
internal sealed partial class FixtureTree : IDisposable
{
    private readonly List<string> deniedFolders = [];
    private readonly List<string> junctions = [];

    public FixtureTree()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TemizlikciTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Path(string relative) => System.IO.Path.Combine(Root, relative);

    public string Folder(string relative)
    {
        string path = Path(relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public string File(string relative, int bytes, DateTime? modifiedUtc = null)
    {
        string path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        Random.Shared.NextBytes(data);
        System.IO.File.WriteAllBytes(path, data);
        if (modifiedUtc is { } date) System.IO.File.SetLastWriteTimeUtc(path, date);
        return path;
    }

    public void HardLink(string existingRelative, string linkRelative)
    {
        string link = Path(linkRelative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(link)!);
        if (!CreateHardLink(link, Path(existingRelative), 0)) throw new IOException($"CreateHardLink failed: {Marshal.GetLastPInvokeError()}");
    }

    /// <summary>A directory junction (no administrator rights needed), like the ones Windows puts in every profile.</summary>
    public void Junction(string linkRelative, string targetAbsolute)
    {
        Run("cmd.exe", "/c", "mklink", "/J", Path(linkRelative), targetAbsolute);
        junctions.Add(Path(linkRelative));
    }

    /// <summary>Takes away the current user's right to list a folder, until the fixture is disposed.</summary>
    public void DenyListing(string relative)
    {
        string folder = Path(relative);
        Run("icacls.exe", folder, "/deny", $"{Environment.UserName}:(RD)");
        deniedFolders.Add(folder);
    }

    /// <summary>The allocation size Windows reports for one file, read independently of the scanner.</summary>
    public static long AllocatedSize(string path)
    {
        using var handle = System.IO.File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (!GetFileInformationByHandleEx(handle, 1, out var info, (uint)Marshal.SizeOf<FileStandardInfo>()))
        {
            throw new IOException($"GetFileInformationByHandleEx failed: {Marshal.GetLastPInvokeError()}");
        }
        return info.AllocationSize;
    }

    public void Dispose()
    {
        foreach (var folder in deniedFolders)
        {
            try
            {
                Run("icacls.exe", folder, "/remove:d", Environment.UserName);
            }
            catch (IOException)
            {
                // Best effort: the delete below reports anything left behind.
            }
        }
        // Junctions go first, one by one, as links: the recursive delete treats a junction like a volume mount point
        // and reports an error for it (without following it).
        foreach (var junction in junctions) Directory.Delete(junction, recursive: false);
        try
        {
            // The fixture's own temporary folder.
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A handle still open in a failing test; %TEMP% is cleaned by Windows eventually.
        }
    }

    private static void Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException($"{executable} failed: {errors}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string fileName, string existingFileName, nint securityAttributes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileStandardInfo info, uint size);
}
