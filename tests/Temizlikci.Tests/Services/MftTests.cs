using Temizlikci.Domain.Scanning;
using Temizlikci.Domain.Tree;
using Temizlikci.Services.Access;
using Temizlikci.Services.Scanning;
using Temizlikci.Services.Scanning.Mft;
using Temizlikci.Tests.Support;

namespace Temizlikci.Tests.Services;

public sealed class MftRecordParserTests
{
    private static readonly DateTime Modified = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private const long Cluster = MftRecordBuilder.ClusterSize;

    private static MftRecord Parse(byte[] onDisk)
    {
        Assert.True(MftRecordParser.ApplyFixups(onDisk));
        Assert.True(MftRecordParser.TryParse(onDisk, Cluster, out var record));
        return record;
    }

    [Fact]
    public void Should_RestoreTheRealBytes_When_ApplyingUpdateSequenceFixups()
    {
        // A name long enough to cross the first 512-byte boundary, where the check value sits on disk.
        string longName = new('n', 240);
        var onDisk = new MftRecordBuilder().StandardInformation(Modified).FileName(longName, 5).Build();

        var record = Parse(onDisk);

        Assert.Equal(longName, record.Name.ToString());
    }

    [Fact]
    public void Should_RejectTheRecord_When_ASectorWasTornDuringAWrite()
    {
        var onDisk = new MftRecordBuilder().FileName("a.txt", 5).Build();
        onDisk[1022] ^= 0xFF;

        Assert.False(MftRecordParser.ApplyFixups(onDisk));
    }

    [Fact]
    public void Should_ReadNameParentSizeAndDate_When_ParsingAFile()
    {
        var record = Parse(new MftRecordBuilder().StandardInformation(Modified).FileName("movie.mp4", 42).Data(2_000).Build());

        Assert.True(record.InUse);
        Assert.False(record.IsDirectory);
        Assert.Equal("movie.mp4", record.Name.ToString());
        Assert.Equal(42, record.ParentRecord);
        Assert.Equal(2_000 * Cluster, record.DataAllocated);
        Assert.Equal(Modified, DateTime.FromFileTimeUtc(record.ModifiedFileTime));
    }

    [Fact]
    public void Should_PreferTheLongName_When_ADosAliasComesFirst()
    {
        var record = Parse(new MftRecordBuilder().FileName("PROGRA~1", 5, space: 2).FileName("Program Files", 5, space: 1).Directory().Build());

        Assert.Equal("Program Files", record.Name.ToString());
        Assert.True(record.IsDirectory);
    }

    [Fact]
    public void Should_UseTheDosName_When_ItIsTheOnlyName()
    {
        var record = Parse(new MftRecordBuilder().FileName("README~1.TXT", 5, space: 2).Build());

        Assert.Equal("README~1.TXT", record.Name.ToString());
    }

    [Fact]
    public void Should_KeepTheFirstLinksParent_When_AFileHasTwoHardLinks()
    {
        var record = Parse(new MftRecordBuilder().FileName("kernel32.dll", 100).FileName("kernel32.dll", 200).Data(256).Build());

        Assert.Equal(100, record.ParentRecord);
    }

    [Fact]
    public void Should_CountResidentDataAsNoClusters_When_AFileFitsInItsRecord()
    {
        var record = Parse(new MftRecordBuilder().FileName("tiny.txt", 5).ResidentData(300).Build());

        Assert.Equal(0, record.DataAllocated);
    }

    [Fact]
    public void Should_CountOnlyRealClusters_When_AStreamIsSparse()
    {
        var sparse = Parse(new MftRecordBuilder().FileName("disk.vhdx", 5).Data(clusters: 768, sparseClusters: 16_000_000, attributeFlags: 0x8000).Build());

        Assert.Equal(768 * Cluster, sparse.DataAllocated);
    }

    [Fact]
    public void Should_IgnoreTheClaimedSize_When_BadClusShadowsTheWholeVolume()
    {
        // The bad-cluster stream is as long as the volume but carries no sparse flag; only its runs tell the truth.
        var bad = Parse(new MftRecordBuilder().FileName("$BadClus", 5).Data(clusters: 0, sparseClusters: 250_000_000, streamName: "$Bad",
            claimedAllocated: 250_000_000 * Cluster).Build());

        Assert.Equal(0, bad.DataAllocated);
    }

    [Fact]
    public void Should_AddNamedStreams_When_AFileHasAlternateData()
    {
        var record = Parse(new MftRecordBuilder().FileName("system.dll", 5).Data(0).Data(10, streamName: "WofCompressedData").Build());

        Assert.Equal(10 * Cluster, record.DataAllocated);
    }

    [Fact]
    public void Should_CountEachSegmentsOwnRuns_When_AStreamContinuesInAnExtensionRecord()
    {
        var record = Parse(new MftRecordBuilder().ExtensionOf(77).Data(300, startingVcn: 12).Build());

        Assert.Equal(77, record.BaseRecord);
        Assert.Equal(300 * Cluster, record.DataAllocated);
    }

    [Fact]
    public void Should_FlagJunctionsButNotCloudFiles_When_ReadingReparsePoints()
    {
        var junction = Parse(new MftRecordBuilder().FileName("Application Data", 5).Directory().Reparse(0xA0000003).Build());
        var cloud = Parse(new MftRecordBuilder().FileName("OneDrive", 5).Directory().Reparse(0x9000701A).Build());

        Assert.True(junction.IsNameSurrogate);
        Assert.False(cloud.IsNameSurrogate);
    }

    [Fact]
    public void Should_SkipTheAttributes_When_TheRecordIsNotInUse()
    {
        var record = Parse(new MftRecordBuilder().Deleted().FileName("gone.txt", 5).Build());

        Assert.False(record.InUse);
        Assert.False(record.HasName);
    }

    [Fact]
    public void Should_DecodeAbsoluteRelativeAndSparseRuns_When_ReadingMappingPairs()
    {
        byte[] runs =
        [
            0x21, 0x18, 0x34, 0x56, // 0x18 clusters at LCN 0x5634
            0x11, 0x08, 0xF0,       // 8 clusters, 16 back: LCN 0x5624
            0x01, 0x04,             // 4 sparse clusters
            0x00,
        ];

        var decoded = MftRecordParser.DecodeRuns(runs);

        Assert.Equal([(0x5634L, 0x18L), (0x5624L, 8L), (-1L, 4L)], decoded);
    }
}

public sealed class MftTreeBuilderTests
{
    private const long Threshold = 10L * 1024 * 1024;
    private static readonly ScanConfiguration Configuration = new() { IndividualFileThreshold = Threshold };

    private static MftRecordTable Table(params (int Number, byte[] OnDisk)[] records)
    {
        var table = new MftRecordTable(64);
        foreach (var (number, onDisk) in records)
        {
            Assert.True(MftRecordParser.ApplyFixups(onDisk));
            Assert.True(MftRecordParser.TryParse(onDisk, MftRecordBuilder.ClusterSize, out var parsed));
            table.Apply(number, parsed, Threshold);
        }
        return table;
    }

    /// <summary>
    /// C:\ (record 5)
    /// ├── $MFT (0, 200 MB)
    /// ├── Users (30)
    /// │   ├── big.iso (31, 20 MB)
    /// │   ├── note.txt (32, 4 KB)
    /// │   └── frag.bin (34, its data in extension record 40, 50 MB)
    /// ├── Application Data (33, a junction)
    /// └── old.txt (35, deleted)
    /// </summary>
    private static MftRecordTable Sample() => Table(
        (0, new MftRecordBuilder().FileName("$MFT", 5).Data((200L << 20) / MftRecordBuilder.ClusterSize).Build()),
        (5, new MftRecordBuilder().Directory().FileName(".", 5).Build()),
        (30, new MftRecordBuilder().Directory().FileName("Users", 5).Build()),
        (31, new MftRecordBuilder().FileName("big.iso", 30).Data((20L << 20) / MftRecordBuilder.ClusterSize).Build()),
        (32, new MftRecordBuilder().FileName("note.txt", 30).Data(1).Build()),
        (33, new MftRecordBuilder().Directory().FileName("Application Data", 5).Reparse(0xA0000003).Build()),
        (34, new MftRecordBuilder().FileName("frag.bin", 30).Build()),
        (35, new MftRecordBuilder().Deleted().FileName("old.txt", 5).Data((1L << 30) / MftRecordBuilder.ClusterSize).Build()),
        (40, new MftRecordBuilder().ExtensionOf(34).Data((50L << 20) / MftRecordBuilder.ClusterSize).Build()));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Should_KeepTheLongName_When_ItLivesInAnExtensionRecordBesideA83Alias(bool baseFirst)
    {
        // The base record holds only the DOS alias; the Win32 name moved to an extension record with the attribute list.
        var baseRecord = (30, new MftRecordBuilder().Directory().FileName("PROFIL~1", 5, space: 2).Build());
        var extension = (41, new MftRecordBuilder().ExtensionOf(30).FileName("Profile 1", 5, space: 1).Build());

        var table = baseFirst ? Table(baseRecord, extension) : Table(extension, baseRecord);

        Assert.Equal("Profile 1", table.Names[30]);
    }

    [Fact]
    public void Should_AskForTheNamesItDidNotKeep_When_AFileGrewThroughAnExtensionRecord()
    {
        var table = Sample();

        Assert.Equal([34], table.MissingNames(Threshold));
    }

    [Fact]
    public void Should_BuildTheSameKindOfTreeAsTheDirectoryWalk_When_ReadingTheTable()
    {
        var table = Sample();
        table.Names[34] = "frag.bin";

        var built = MftTreeBuilder.Build(table, MftTreeBuilder.RootRecord, @"C:\", Configuration, TestContext.Current.CancellationToken);

        Assert.Equal(@"C:\", built.Root.Name);
        Assert.Equal((200L << 20) + (20L << 20) + 4096 + (50L << 20), built.Root.AllocatedSize);
        Assert.Equal(["$MFT", "Users"], built.Root.Children.Select(child => child.Name));
        var users = built.Root.Children.Single(child => child.Name == "Users");
        Assert.Equal(["frag.bin", "big.iso", string.Empty], users.Children.Select(child => child.Name));
        Assert.Equal(NodeKind.SmallerFiles, users.Children[2].Kind);
        Assert.Equal(4, built.FileCount);
    }

    [Fact]
    public void Should_BuildOnlyTheFolder_When_TheScanStartsBelowTheRoot()
    {
        var table = Sample();

        int? users = MftTreeBuilder.RecordFor(table, @"C:\", @"c:\USERS");
        var built = MftTreeBuilder.Build(table, users!.Value, @"C:\Users", Configuration, TestContext.Current.CancellationToken);

        Assert.Equal(30, users);
        Assert.Equal(@"C:\Users", built.Root.Name);
        Assert.Null(MftTreeBuilder.RecordFor(table, @"C:\", @"C:\Application Data\x"));
    }
}

public sealed class MftIntegrationTests
{
    /// <summary>Only an elevated test run can open the volume; unelevated runs skip with a reason.</summary>
    [Fact]
    public async Task Should_AgreeWithTheDirectoryWalk_When_BothMeasureTheSameFolder()
    {
        var elevation = new WindowsElevation();
        Assert.SkipUnless(elevation.IsElevated, "Reading the Master File Table needs administrator rights.");
        WindowsElevation.EnableBackupPrivilege();
        using var tree = new FixtureTree();
        for (int index = 0; index < 50; index++) tree.File($@"f{index % 5}\s{index}.bin", 20_000 + index * 1_000);
        tree.File("big.bin", 12 * 1024 * 1024);
        tree.HardLink("big.bin", @"f1\big-link.bin");

        var mft = await Finish(new MftScanner(), tree.Root);
        var walk = await Finish(new DirectoryScanner(), tree.Root);

        Assert.Equal(ScanMethod.MasterFileTable, mft.Method);
        Assert.Equal(walk.Root.AllocatedSize, mft.Root.AllocatedSize);
    }

    [Fact]
    public async Task Should_WalkDirectories_When_TheProcessIsNotElevated()
    {
        using var tree = new FixtureTree();
        tree.File("a.bin", 20_000);
        var scanner = new AdaptiveScanner(() => false, new DirectoryScanner(), new MftScanner());

        var result = await Finish(scanner, tree.Root);

        Assert.Equal(ScanMethod.DirectoryWalk, result.Method);
    }

    [Fact]
    public async Task Should_FallBackToTheDirectoryWalk_When_TheMasterFileTableCannotBeOpened()
    {
        using var tree = new FixtureTree();
        tree.File("a.bin", 20_000);
        // Claims elevation without having it, so opening the volume fails the way a broken table would.
        var scanner = new AdaptiveScanner(() => true, new DirectoryScanner(), new MftScanner());
        Assert.SkipWhen(new WindowsElevation().IsElevated, "Elevated runs can open the volume, so nothing falls back.");

        var result = await Finish(scanner, tree.Root);

        Assert.Equal(ScanMethod.DirectoryWalk, result.Method);
        Assert.Equal(FixtureTree.AllocatedSize(tree.Path("a.bin")), result.Root.AllocatedSize);
    }

    private static async Task<ScanResult> Finish(IDiskScanner scanner, string root)
    {
        ScanResult? result = null;
        await foreach (var scanEvent in scanner.ScanAsync(root, ScanConfiguration.Standard, TestContext.Current.CancellationToken))
        {
            if (scanEvent is ScanEvent.Finished finished) result = finished.Result;
        }
        return result ?? throw new InvalidOperationException("The scan ended without a result.");
    }
}
