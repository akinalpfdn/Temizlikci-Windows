using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;
using Temizlikci.Services.Scanning;

namespace Temizlikci.Tests.Services;

/// <summary>
/// Measures the scanner on a real folder against an independent total. Skipped unless TEMIZLIKCI_BENCH_PATH names a
/// folder; it only reads. Run: <c>$env:TEMIZLIKCI_BENCH_PATH="C:\Users\me\source"; dotnet test … --filter-class *ScanBenchmarkTests</c>.
/// </summary>
public sealed partial class ScanBenchmarkTests
{
    private static string? BenchmarkPath => Environment.GetEnvironmentVariable("TEMIZLIKCI_BENCH_PATH");

    [Fact]
    public async Task Should_AgreeWithAnIndependentTotalWithinOnePercent_When_ScanningARealFolder()
    {
        Assert.SkipWhen(BenchmarkPath is null, "Set TEMIZLIKCI_BENCH_PATH to a folder to measure.");
        string root = NodePath.Trim(BenchmarkPath!);

        var clock = Stopwatch.StartNew();
        ScanResult? result = null;
        await foreach (var scanEvent in new DirectoryScanner().ScanAsync(root, ScanConfiguration.Standard, TestContext.Current.CancellationToken))
        {
            if (scanEvent is ScanEvent.Finished finished) result = finished.Result;
        }
        var scanTime = clock.Elapsed;
        long retained = GC.GetTotalMemory(forceFullCollection: true);
        long peak = Process.GetCurrentProcess().PeakWorkingSet64;

        clock.Restart();
        var (oracleTotal, oracleFiles, unreadable) = IndependentTotal(root);
        var oracleTime = clock.Elapsed;

        Assert.NotNull(result);
        double difference = oracleTotal == 0 ? 0 : Math.Abs(result.Root.AllocatedSize - oracleTotal) / (double)oracleTotal;
        string report =
            $"{root}: scanner {result.Root.AllocatedSize:N0} B, {result.FileCount:N0} files, {result.DirectoryCount:N0} folders, "
            + $"{result.InaccessibleCount} unreadable, {scanTime.TotalSeconds:F2} s; independent {oracleTotal:N0} B, {oracleFiles:N0} files, "
            + $"{unreadable} unreadable, {oracleTime.TotalSeconds:F2} s; difference {difference:P3}; managed after scan {retained / 1048576.0:F0} MB, "
            + $"peak working set {peak / 1048576.0:F0} MB";
        TestContext.Current.SendDiagnosticMessage(report);
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "temizlikci-benchmark.txt"), report + Environment.NewLine);
        Assert.True(difference < 0.01, $"Scanner and independent totals differ by {difference:P2}.");
    }

    /// <summary>Elevated only: the Master File Table and the directory walk (with backup rights) on the same folder.</summary>
    [Fact]
    public async Task Should_MatchTheDirectoryWalk_When_ReadingTheMasterFileTableOfARealFolder()
    {
        Assert.SkipWhen(BenchmarkPath is null, "Set TEMIZLIKCI_BENCH_PATH to a folder to measure.");
        Assert.SkipUnless(new Temizlikci.Services.Access.WindowsElevation().IsElevated, "Reading the Master File Table needs administrator rights.");
        Temizlikci.Services.Access.WindowsElevation.EnableBackupPrivilege();
        string root = NodePath.Trim(BenchmarkPath!);

        var clock = Stopwatch.StartNew();
        var mft = await Finish(new MftScanner(), root);
        var mftTime = clock.Elapsed;
        long retained = GC.GetTotalMemory(forceFullCollection: true);
        clock.Restart();
        var walk = await Finish(new DirectoryScanner(), root);
        var walkTime = clock.Elapsed;

        double difference = walk.Root.AllocatedSize == 0 ? 0 : Math.Abs(mft.Root.AllocatedSize - walk.Root.AllocatedSize) / (double)walk.Root.AllocatedSize;
        string report = $"{root} elevated: MFT {mft.Root.AllocatedSize:N0} B, {mft.FileCount:N0} files, {mftTime.TotalSeconds:F2} s, managed {retained / 1048576.0:F0} MB; "
            + $"walk {walk.Root.AllocatedSize:N0} B, {walk.FileCount:N0} files, {walk.InaccessibleCount} unreadable, {walkTime.TotalSeconds:F2} s; difference {difference:P3}";
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "temizlikci-benchmark.txt"), report + Environment.NewLine);
        Assert.True(difference < 0.01, report);
    }

    private static async Task<ScanResult> Finish(IDiskScanner scanner, string root)
    {
        ScanResult? result = null;
        await foreach (var scanEvent in scanner.ScanAsync(root, ScanConfiguration.Standard, TestContext.Current.CancellationToken))
        {
            if (scanEvent is ScanEvent.Finished finished) result = finished.Result;
        }
        return result ?? throw new InvalidOperationException("No result.");
    }

    /// <summary>Opens every file for its standard information, counting hard links once by file ID — slow, but shares
    /// no code with the scanner.</summary>
    private static (long Total, long Files, int Unreadable) IndependentTotal(string root)
    {
        var seen = new HashSet<(ulong, ulong)>();
        long total = 0;
        long files = 0;
        int unreadable = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                unreadable++;
                continue;
            }
            foreach (var entry in entries)
            {
                bool isReparse = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
                if (entry is DirectoryInfo directory)
                {
                    if (!isReparse || directory.LinkTarget is null) pending.Push(directory.FullName);
                    continue;
                }
                files++;
                using var handle = CreateFile(@"\\?\" + entry.FullName, 0x80, 7, 0, 3, 0x00200000, 0);
                if (handle.IsInvalid) continue;
                if (!GetFileInformationByHandleEx(handle, 1, out StandardInfo info, (uint)Marshal.SizeOf<StandardInfo>())) continue;
                if (info.NumberOfLinks > 1 && GetFileInformationByHandleEx(handle, 18, out IdInfo id, (uint)Marshal.SizeOf<IdInfo>()))
                {
                    if (!seen.Add((id.Low, id.High))) continue;
                }
                total += info.AllocationSize;
            }
        }
        return (total, files, unreadable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong Low;
        public ulong High;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out StandardInfo info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out IdInfo info, uint size);
}
