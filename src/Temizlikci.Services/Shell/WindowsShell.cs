using System.Diagnostics;
using System.Runtime.InteropServices;
using Temizlikci.Domain.Actions;

namespace Temizlikci.Services.Shell;

/// <summary>Hands people over to Explorer, the Properties dialog, Settings and the browser.</summary>
public sealed partial class WindowsShell : IShell
{
    private const uint ShopFilePath = 0x2;

    public void ShowInExplorer(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        // "/select," takes the path as part of the same argument, so it can't go through ArgumentList.
        var start = new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = false };
        using var _ = Process.Start(start);
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

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHObjectProperties(nint window, uint type, string objectName, string? propertyPage);
}
