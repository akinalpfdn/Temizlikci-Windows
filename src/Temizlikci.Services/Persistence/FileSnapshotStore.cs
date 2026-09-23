using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Tree;

namespace Temizlikci.Services.Persistence;

/// <summary>Stores snapshots as Brotli-compressed JSON, one folder per location, file names in milliseconds.</summary>
public sealed class FileSnapshotStore : ISnapshotStore
{
    private readonly string directory;

    public FileSnapshotStore(string? directory = null)
    {
        this.directory = directory ?? AppFolders.Snapshots;
    }

    public IReadOnlyList<ScanSnapshot> Recent(string locationPath, int limit) =>
        Files(locationPath).AsEnumerable().Reverse().Take(limit).Select(Read).ToList();

    public void Save(ScanSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var stored = new Stored
        {
            Version = snapshot.Version,
            LocationPath = snapshot.LocationPath,
            DateUtc = snapshot.DateUtc,
            Sizes = new Dictionary<string, long>(snapshot.Sizes),
            UnreadPaths = [.. snapshot.UnreadPaths],
        };
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            JsonSerializer.Serialize(brotli, stored);
        }
        long milliseconds = new DateTimeOffset(snapshot.DateUtc.ToUniversalTime()).ToUnixTimeMilliseconds();
        string name = milliseconds.ToString(CultureInfo.InvariantCulture) + ".snapshot";
        AppFolders.WriteAtomically(Path.Join(FolderFor(snapshot.LocationPath), name), compressed.ToArray());
    }

    public void Prune(string locationPath, int count)
    {
        var files = Files(locationPath);
        // Snapshots are the app's own history, not the person's files, so removing old ones directly is intended.
        foreach (var old in files.Take(Math.Max(0, files.Count - count))) File.Delete(old);
    }

    /// <summary>Snapshot files of a location, oldest first (names are millisecond timestamps).</summary>
    private List<string> Files(string locationPath)
    {
        string folder = FolderFor(locationPath);
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*.snapshot")
            .OrderBy(file => long.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : 0)
            .ToList();
    }

    private static ScanSnapshot Read(string file)
    {
        using var input = File.OpenRead(file);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        var stored = JsonSerializer.Deserialize<Stored>(brotli) ?? throw new InvalidDataException($"{file} is empty.");
        return new ScanSnapshot(stored.Version, stored.LocationPath, DateTime.SpecifyKind(stored.DateUtc, DateTimeKind.Utc),
            new Dictionary<string, long>(stored.Sizes, NodePath.Comparer), new HashSet<string>(stored.UnreadPaths, NodePath.Comparer));
    }

    private string FolderFor(string locationPath) => Path.Join(directory, AppFolders.KeyFor(locationPath));

    private sealed class Stored
    {
        public int Version { get; set; }
        public string LocationPath { get; set; } = string.Empty;
        public DateTime DateUtc { get; set; }
        public Dictionary<string, long> Sizes { get; set; } = [];
        public List<string> UnreadPaths { get; set; } = [];
    }
}
