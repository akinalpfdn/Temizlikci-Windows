using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Temizlikci.Services.Scanning.Native;

/// <summary>The Win32 and NT calls the scanners need. Kept in one place so the unsafe surface is easy to review.</summary>
internal static partial class NativeMethods
{
    public const uint FileListDirectory = 0x0001;
    public const uint FileReadAttributes = 0x0080;
    public const uint Synchronize = 0x00100000;
    public const uint GenericRead = 0x80000000;
    public const uint FileShareAll = 0x00000007;
    public const uint OpenExisting = 3;
    public const uint FileFlagBackupSemantics = 0x02000000;
    public const uint FileFlagOpenReparsePoint = 0x00200000;
    public const uint FileFlagNoBuffering = 0x20000000;

    public const uint FileAttributeDirectory = 0x10;
    public const uint FileAttributeReparsePoint = 0x400;
    public const uint InvalidFileAttributes = 0xFFFFFFFF;

    /// <summary>Set in the reparse tag of junctions, symbolic links and mount points: the item stands for another path.</summary>
    public const uint ReparseTagNameSurrogate = 0x20000000;
    /// <summary>Windows Overlay Filter: CompactOS-compressed system files keep their data in an alternate stream.</summary>
    public const uint ReparseTagWof = 0x80000017;

    public const int FileIdFullDirectoryInformation = 38;
    public const int StatusNoMoreFiles = unchecked((int)0x80000006);
    public const int StatusBufferOverflow = unchecked((int)0x80000005);

    public const int ErrorFileNotFound = 2;
    public const int ErrorPathNotFound = 3;
    public const int ErrorAccessDenied = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct IoStatusBlock
    {
        public nint Status;
        public nuint Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Win32FileAttributeData
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("ntdll.dll")]
    public static unsafe partial int NtQueryDirectoryFile(SafeFileHandle fileHandle, nint eventHandle, nint apcRoutine, nint apcContext,
        out IoStatusBlock ioStatusBlock, void* fileInformation, uint length, int fileInformationClass,
        [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry, nint fileName, [MarshalAs(UnmanagedType.U1)] bool restartScan);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileAttributesEx(string fileName, int infoLevelId, out Win32FileAttributeData fileInformation);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetCompressedFileSize(string fileName, out uint fileSizeHigh);

    /// <summary>A path Win32 accepts past MAX_PATH: <c>\\?\C:\…</c>.</summary>
    public static string LongPath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..]
        : @"\\?\" + path;
}
