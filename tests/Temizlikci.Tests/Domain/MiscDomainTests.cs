using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Identity;
using Temizlikci.Domain.Tree;
using Temizlikci.Domain.Updates;
using Temizlikci.Domain.Volumes;
using static Temizlikci.Tests.Support.TreeBuilder;

namespace Temizlikci.Tests.Domain;

public sealed class AppVersionTests
{
    [Fact]
    public void Should_OrderByNumber_When_IgnoringALeadingVAndMissingParts()
    {
        Assert.True(AppVersion.Parse("v0.2.0")! > AppVersion.Parse("0.1.9")!);
        Assert.True(AppVersion.Parse("1.10")! > AppVersion.Parse("1.9.9")!);
        Assert.Equal(AppVersion.Parse("1.4"), AppVersion.Parse("1.4.0"));
        Assert.Equal(AppVersion.Parse("1.4")!.GetHashCode(), AppVersion.Parse("1.4.0")!.GetHashCode());
        Assert.Equal(AppVersion.Parse("2.0.0"), AppVersion.Parse("v2.0.0-beta.1"));
        Assert.Null(AppVersion.Parse("latest"));
    }

    [Fact]
    public void Should_PreferTheAttachedZip_When_ReadingGitHubsResponse()
    {
        const string json = """
            {"tag_name":"v0.2.0","html_url":"https://github.com/akinalpfdn/Temizlikci-Windows/releases/tag/v0.2.0",
             "draft":false,"prerelease":false,
             "assets":[{"name":"notes.txt","browser_download_url":"https://example.com/notes.txt"},
                       {"name":"Temizlikci-0.2.0-win-x64.zip","browser_download_url":"https://example.com/Temizlikci-0.2.0-win-x64.zip"}]}
            """;

        var release = Release.FromGitHub(json);

        Assert.NotNull(release);
        Assert.Equal(AppVersion.Parse("0.2.0"), release.Version);
        Assert.EndsWith("Temizlikci-0.2.0-win-x64.zip", release.DownloadUrl.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_PreferTheInstaller_When_BothInstallerAndZipAreAttached()
    {
        const string json = """
            {"tag_name":"v1.1.0","html_url":"https://github.com/akinalpfdn/Temizlikci-Windows/releases/tag/v1.1.0",
             "assets":[{"name":"Temizlikci-1.1.0-win-x64-portable.zip","browser_download_url":"https://example.com/portable.zip"},
                       {"name":"Temizlikci-Setup.exe","browser_download_url":"https://example.com/Temizlikci-Setup.exe"}]}
            """;

        var release = Release.FromGitHub(json);

        Assert.Equal("https://example.com/Temizlikci-Setup.exe", release?.DownloadUrl.AbsoluteUri);
    }

    [Fact]
    public void Should_FallBackToTheReleasePage_When_NothingIsAttached()
    {
        var release = Release.FromGitHub("""{"tag_name":"v0.3.0","html_url":"https://github.com/x/y/releases/tag/v0.3.0","assets":[]}""");

        Assert.Equal("https://github.com/x/y/releases/tag/v0.3.0", release?.DownloadUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""{"tag_name":"v1.0.0","html_url":"https://x/y","draft":true}""")]
    [InlineData("<html>rate limited</html>")]
    public void Should_FindNoRelease_When_TheResponseIsNotAUsableRelease(string json)
    {
        Assert.Null(Release.FromGitHub(json));
    }
}

public sealed class SpaceBreakdownTests
{
    private const long Gb = 1_000_000_000;

    [Fact]
    public void Should_NameTheHiddenPartsAndKeepTheRestHonest_When_BreakingDownOtherSpace()
    {
        var breakdown = SpaceBreakdown.Make(60 * Gb, new HiddenSpace(FileSystemMetadata: 3 * Gb, ShadowCopies: 12 * Gb), hasUnreadableFolders: false);

        Assert.Equal([SpacePartKind.ShadowCopies, SpacePartKind.FileSystemMetadata, SpacePartKind.Remainder], breakdown.Parts.Select(p => p.Kind));
        Assert.Equal([12 * Gb, 3 * Gb, 45 * Gb], breakdown.Parts.Select(p => p.Size));
        Assert.Equal(breakdown.Total, breakdown.Parts.Sum(p => p.Size));
    }

    [Fact]
    public void Should_NeverLeaveANegativeRemainder_When_NamedPartsExceedTheEstimate()
    {
        var breakdown = SpaceBreakdown.Make(10 * Gb, new HiddenSpace(4 * Gb, 20 * Gb), hasUnreadableFolders: true);

        Assert.DoesNotContain(breakdown.Parts, part => part.Kind == SpacePartKind.Remainder);
        Assert.Equal(24 * Gb, breakdown.Total);
        Assert.True(breakdown.HasUnreadableFolders);
    }

    [Fact]
    public void Should_ShowOnlyTheRemainder_When_NothingCouldBeRead()
    {
        var breakdown = SpaceBreakdown.Make(20 * Gb, new HiddenSpace(null, null), hasUnreadableFolders: true);

        Assert.Equal([new SpacePart(SpacePartKind.Remainder, 20 * Gb)], breakdown.Parts);
    }

    [Fact]
    public void Should_MeasureUnattributedSpaceFromUsedCapacity_When_AScanCoversTheVolume()
    {
        var usage = new VolumeUsage(TotalCapacity: 500 * Gb, AvailableCapacity: 60 * Gb);

        Assert.Equal(440 * Gb, usage.UsedCapacity);
        Assert.Equal(40 * Gb, usage.Unattributed(400 * Gb));
        Assert.Equal(0, usage.Unattributed(460 * Gb));
    }
}

public sealed class FolderIdentityTests
{
    private sealed class StubApps(Dictionary<string, string> folders, Dictionary<string, string> packages) : IInstalledApps
    {
        public string? NameForFolder(string folderName) => folders.GetValueOrDefault(folderName);

        public string? NameForPackage(string packageFamilyName) => packages.GetValueOrDefault(packageFamilyName);
    }

    private sealed class StubTypes : IFileTypeNames
    {
        public string? Describe(string extension) => extension == "pdf" ? "PDF Document" : null;
    }

    private static FolderIdentifier Identifier() => new(
        KnownLocations.Sample,
        new StubApps(new() { ["Slack"] = "Slack", ["JetBrains"] = "JetBrains Toolbox" }, new() { ["Microsoft.WindowsTerminal_8wekyb3d8bbwe"] = "Terminal" }),
        new StubTypes());

    [Theory]
    [InlineData(@"C:\Windows\WinSxS", KnownFolder.WinSxS)]
    [InlineData(@"C:\Users\dev\AppData\Local", KnownFolder.LocalAppData)]
    [InlineData(@"c:\users\DEV\downloads", KnownFolder.Downloads)]
    [InlineData(@"C:\", KnownFolder.SystemDrive)]
    [InlineData(@"D:\", KnownFolder.OtherDrive)]
    [InlineData(@"D:\$Recycle.Bin", KnownFolder.RecycleBin)]
    [InlineData(@"C:\hiberfil.sys", KnownFolder.HibernationFile)]
    public void Should_ExplainWhatWindowsDefines_When_ThePathIsKnown(string path, KnownFolder expected)
    {
        var identity = Identifier().Identify(Dir("x"), path);

        Assert.Equal(new FolderIdentity(IdentitySource.System, expected), identity);
    }

    [Fact]
    public void Should_NameTheAppBehindASupportFolder_When_ItIsInstalled()
    {
        var identifier = Identifier();

        Assert.Equal("Slack", identifier.Identify(Dir("Slack"), @"C:\Users\dev\AppData\Roaming\Slack")?.AppName);
        Assert.Equal("Terminal", identifier.Identify(Dir("x"), @"C:\Users\dev\AppData\Local\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe")?.AppName);
        Assert.Null(identifier.Identify(Dir("x"), @"C:\Users\dev\AppData\Local\UnknownTool"));
        Assert.Null(identifier.Identify(Dir("x"), @"C:\Users\dev\Work\Slack"));
    }

    [Fact]
    public void Should_DescribeAFileByItsType_When_TheTypeIsRegistered()
    {
        var identifier = Identifier();

        Assert.Equal(new FolderIdentity(IdentitySource.FileType, TypeName: "PDF Document"), identifier.Identify(File("paper.pdf", 1), @"C:\Users\dev\Documents\paper.pdf"));
        Assert.Null(identifier.Identify(File("blob.xyz", 1), @"C:\Users\dev\blob.xyz"));
    }

    [Fact]
    public void Should_ExpandTokens_When_ReadingARulePattern()
    {
        var locations = KnownLocations.Sample;

        Assert.Equal(@"C:\Users\dev\.nuget\packages", locations.Expand(@"~\.nuget\packages"));
        Assert.Equal(@"C:\Users\dev\AppData\Local\Temp", locations.Expand(@"%LOCALAPPDATA%\Temp"));
        Assert.Equal(@"C:\Windows.old", locations.Expand(@"%SYSTEMDRIVE%\Windows.old"));
        Assert.Equal(@"C:\Users", locations.Users);
        Assert.Equal(@"D:\x", locations.Expand(@"D:\x\"));
    }

    [Fact]
    public void Should_BeAnItem_When_TheNodeIsAFileFolderOrUnreadableFolder()
    {
        Assert.True(File("a", 1).IsItem);
        Assert.True(FileNode.Inaccessible("b").IsItem);
        Assert.False(FileNode.SmallerFiles(1, 1).IsItem);
    }
}
