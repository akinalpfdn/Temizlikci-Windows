using System.Diagnostics;
using System.Runtime.InteropServices;
using Temizlikci.Domain.Actions;

namespace Temizlikci.Services.Shell;

/// <summary>Hands people over to Explorer, the Properties dialog, Settings and the browser.</summary>
public sealed partial class WindowsShell : IShell
{
    private const uint ShopFilePath = 0x2;
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Opens the item's folder with the item selected. The shell API asks the Explorer that is already running, which
    /// works from an elevated app too; starting explorer.exe from an elevated process can open a default window instead.
    /// </summary>
    public void ShowInExplorer(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (SHParseDisplayName(path, 0, out nint item, 0, out _) == 0 && item != 0)
        {
            try
            {
                if (SHOpenFolderAndSelectItems(item, 0, 0, 0) == 0) return;
            }
            finally
            {
                Marshal.FreeCoTaskMem(item);
            }
        }
        // The shell couldn't parse or open it (a path that vanished since the scan): Explorer shows what it can.
        var start = new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = false };
        using var explorer = Process.Start(start);
    }

    public void ShowProperties(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        // The dialog belongs to the shell and runs on its own; a false return means the item vanished since the scan.
        SHObjectProperties(0, ShopFilePath, path, null);
    }

    public void OpenUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        using var _ = Process.Start(new ProcessStartInfo(uri.OriginalString) { UseShellExecute = true });
    }

    public void OpenSystemProtection()
    {
        // The dialog asks for administrator rights itself; only the shell can show that prompt for it.
        var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "SystemPropertiesProtection.exe")) { UseShellExecute = true };
        try
        {
            using var _ = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            // The person chose No on the UAC prompt: nothing to open.
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHObjectProperties(nint window, uint type, string objectName, string? propertyPage);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, nint bindContext, out nint itemIdList, uint attributesIn, out uint attributesOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenFolderAndSelectItems(nint folder, uint count, nint items, uint flags);
}
