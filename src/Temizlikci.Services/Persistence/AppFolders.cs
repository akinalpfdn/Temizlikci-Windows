using System.Security.Cryptography;
using System.Text;

namespace Temizlikci.Services.Persistence;

/// <summary>Where the app keeps its own data: %LOCALAPPDATA%\Temizlikci.</summary>
public static class AppFolders
{
    public static string Root { get; } = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temizlikci");

    public static string Scans => Path.Join(Root, "Scans");

    public static string Snapshots => Path.Join(Root, "Snapshots");

    public static string Settings => Path.Join(Root, "settings.json");

    /// <summary>A short, stable file name for a location, the same whatever the path's case.</summary>
    public static string KeyFor(string locationPath)
    {
        ArgumentNullException.ThrowIfNull(locationPath);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(locationPath.ToUpperInvariant()));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    /// <summary>Writes a file so a crash never leaves half of it behind: a temporary file, then a replace.</summary>
    public static void WriteAtomically(string path, ReadOnlySpan<byte> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, data.ToArray());
        File.Move(temporary, path, overwrite: true);
    }
}
