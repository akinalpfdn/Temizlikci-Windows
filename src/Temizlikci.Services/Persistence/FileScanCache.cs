using System.IO.Compression;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Tree;

namespace Temizlikci.Services.Persistence;

/// <summary>Stores each location's last scan as one Brotli-compressed archive in the app's own folder.</summary>
public sealed class FileScanCache : IScanCache
{
    private readonly string directory;

    public FileScanCache(string? directory = null)
    {
        this.directory = directory ?? AppFolders.Scans;
    }

    public void Save(FileNode root, DateTime scannedAtUtc, string locationPath)
    {
        var archive = ScanArchive.Encode(root, scannedAtUtc, locationPath);
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(archive);
        }
        AppFolders.WriteAtomically(FileFor(locationPath), compressed.ToArray());
    }

    public ScanArchive.Archived? Load(string locationPath)
    {
        string path = FileFor(locationPath);
        if (!File.Exists(path)) return null;
        using var input = File.OpenRead(path);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var data = new MemoryStream();
        brotli.CopyTo(data);
        var archived = ScanArchive.Decode(data.GetBuffer().AsSpan(0, (int)data.Length));
        // A cache written for another folder is not usable, however it got there.
        return NodePath.Comparer.Equals(archived.LocationPath, locationPath) ? archived : null;
    }

    public void Remove(string locationPath)
    {
        // The cache is the app's own data, not the person's, so removing it directly is intended.
        File.Delete(FileFor(locationPath));
    }

    private string FileFor(string locationPath) => Path.Join(directory, AppFolders.KeyFor(locationPath) + ".scan");
}
