using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Temizlikci.Domain.Identity;

namespace Temizlikci.Services.Identity;

/// <summary>
/// Installed programs from the uninstall registry (display name and publisher) and Store apps from the package
/// catalog. Read once, off the UI thread, the first time anything asks.
/// </summary>
public sealed class InstalledApps : IInstalledApps
{
    private readonly Lazy<Task<Catalog>> catalog = new(() => Task.Run(Load));

    /// <summary>Starts reading in the background, so the first lookup doesn't wait.</summary>
    public void Warm() => _ = catalog.Value;

    public string? NameForFolder(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        if (!TryCatalog(out var loaded)) return null;
        if (loaded.ByName.TryGetValue(folderName, out var exact)) return exact;
        return loaded.Publishers.TryGetValue(folderName, out var publisher) ? publisher : null;
    }

    public string? NameForPackage(string packageFamilyName)
    {
        ArgumentNullException.ThrowIfNull(packageFamilyName);
        return TryCatalog(out var loaded) && loaded.Packages.TryGetValue(packageFamilyName, out var name) ? name : null;
    }

    /// <summary>Answers only once the catalog is read; until then folders simply aren't recognized as apps yet.</summary>
    private bool TryCatalog(out Catalog loaded)
    {
        var task = catalog.Value;
        if (task.IsCompletedSuccessfully)
        {
            loaded = task.Result;
            return true;
        }
        loaded = Catalog.Empty;
        return false;
    }

    private sealed record Catalog(IReadOnlyDictionary<string, string> ByName, IReadOnlyDictionary<string, string> Publishers, IReadOnlyDictionary<string, string> Packages)
    {
        public static Catalog Empty { get; } = new(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());
    }

    private static Catalog Load()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var publishers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(uninstall);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var entry = key.OpenSubKey(sub);
                if (entry?.GetValue("DisplayName") is not string name || name.Length == 0) continue;
                // A folder named after the program's first word ("Slack", "Docker") belongs to it.
                byName.TryAdd(name, name);
                string firstWord = name.Split(' ')[0];
                if (firstWord.Length >= 3) byName.TryAdd(firstWord, name);
                if (entry.GetValue("Publisher") is string publisher && publisher.Length > 0)
                {
                    string shortPublisher = publisher.Split(',', ' ')[0];
                    if (shortPublisher.Length >= 3) publishers.TryAdd(shortPublisher, publisher);
                }
            }
        }
        return new Catalog(byName, publishers, StorePackages());
    }

    private static Dictionary<string, string> StorePackages()
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            foreach (var package in manager.FindPackagesForUser(string.Empty))
            {
                string display = package.DisplayName;
                if (string.IsNullOrWhiteSpace(display) || display.StartsWith("ms-resource:", StringComparison.Ordinal)) continue;
                packages.TryAdd(package.Id.FamilyName, display);
            }
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or ArgumentException)
        {
            // Without the package catalog, Store app folders just aren't named.
            System.Diagnostics.Trace.TraceWarning($"Reading Store packages failed: {exception.Message}");
        }
        return packages;
    }
}

/// <summary>File type names the way Explorer shows them ("Text Document"), from the shell.</summary>
public sealed partial class ShellFileTypeNames : IFileTypeNames
{
    private const uint ShgfiTypeName = 0x400;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private readonly ConcurrentDictionary<string, string?> cache = new(StringComparer.OrdinalIgnoreCase);

    public string? Describe(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return cache.GetOrAdd(extension, static ext =>
        {
            var info = new ShFileInfo();
            nint result = SHGetFileInfo("file." + ext, FileAttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiTypeName | ShgfiUseFileAttributes);
            if (result == 0 || string.IsNullOrWhiteSpace(info.TypeName)) return null;
            // The shell's fallback for unknown types ("XYZ File") says nothing the extension doesn't.
            return info.TypeName.Equals($"{ext.ToUpperInvariant()} File", StringComparison.OrdinalIgnoreCase) ? null : info.TypeName;
        });
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

#pragma warning disable SYSLIB1054 // ByValTStr structs need the built-in marshaller; LibraryImport can't generate it.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);
#pragma warning restore SYSLIB1054
}
