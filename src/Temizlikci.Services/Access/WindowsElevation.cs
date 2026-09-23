using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Temizlikci.Domain.Actions;

namespace Temizlikci.Services.Access;

/// <summary>Administrator rights for the whole app, asked for with the person's consent (DECISIONS 2026-09-23).</summary>
public sealed partial class WindowsElevation : IElevation
{
    private const int ErrorCancelled = 1223;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint PrivilegeEnabled = 0x00000002;

    /// <summary>Passed to the elevated instance so it knows it was started on purpose.</summary>
    public const string RestartedArgument = "--elevated";

    public bool IsElevated { get; } = CheckElevated();

    public bool RestartElevated()
    {
        string? executable = Environment.ProcessPath;
        if (executable is null) return false;
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add(RestartedArgument);
        try
        {
            Process.Start(start);
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            // The person chose No on the UAC prompt: nothing changes.
            return false;
        }
    }

    /// <summary>
    /// Lets an elevated process read every folder, whatever its permissions say (the right backup programs use).
    /// Directory handles are opened with backup semantics, so this is all it takes. Returns false when not elevated.
    /// </summary>
    public static bool EnableBackupPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out nint token)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, "SeBackupPrivilege", out long luid)) return false;
            var privileges = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = PrivilegeEnabled };
            return AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0) && Marshal.GetLastPInvokeError() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static bool CheckElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges newState, uint bufferLength, nint previousState, nint returnLength);
}
