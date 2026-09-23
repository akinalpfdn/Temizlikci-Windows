using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Tree;
using Temizlikci.Tests.Support;
using static Temizlikci.Tests.Support.TreeBuilder;

namespace Temizlikci.Tests.Domain;

/// <summary>Marker files that exist only for the test, by folder path. Globs like *.csproj match by extension.</summary>
internal sealed class StubMarkers(Dictionary<string, string[]> contents) : IMarkerChecker
{
    public StubMarkers()
        : this([])
    {
    }

    public bool Contains(string folder, string marker) => FirstMatch(folder, marker) is not null;

    public string? FirstMatch(string folder, string marker)
    {
        var files = contents.FirstOrDefault(entry => NodePath.Comparer.Equals(entry.Key, NodePath.Trim(folder))).Value ?? [];
        string? found = marker.StartsWith("*.", StringComparison.Ordinal)
            ? files.FirstOrDefault(file => file.EndsWith(marker[1..], StringComparison.OrdinalIgnoreCase))
            : files.FirstOrDefault(file => string.Equals(file, marker, StringComparison.OrdinalIgnoreCase));
        return found is null ? null : NodePath.Join(folder, found);
    }
}

public sealed class RuleEngineTests
{
    private static readonly KnownLocations Locations = KnownLocations.Sample;

    private static RuleEngine Engine(Dictionary<string, string[]>? markers = null) =>
        new(CleanupCatalog.Rules, Locations, new StubMarkers(markers ?? []));

    private static FileNode Tree(params string[] paths) => Containing(@"C:\", paths);

    [Fact]
    public void Should_RecognizeCachesByTheirHomeRelativePaths_When_MatchingATree()
    {
        var matches = Engine().Matches(Tree(@"C:\Users\dev\.nuget\packages\newtonsoft", @"C:\Users\dev\AppData\Local\NuGet\v3-cache\x"));

        Assert.Equal(new HashSet<string> { "dotnet.packages", "dotnet.httpCache" }, matches.Select(m => m.Rule.Id).ToHashSet());
        var packages = matches.Single(m => m.Rule.Id == "dotnet.packages");
        Assert.Equal(SafetyLevel.Tool, packages.Rule.Safety);
        Assert.Equal([@"C:\", @"C:\Users", @"C:\Users\dev", @"C:\Users\dev\.nuget", @"C:\Users\dev\.nuget\packages"], packages.IdPath);
    }

    [Fact]
    public void Should_MatchProjectFoldersOnlyNextToTheirMarker_When_TheNameIsCommon()
    {
        var tree = Tree(@"C:\Work\app\build\x", @"C:\Work\app\.dart_tool", @"C:\Work\website\build\y");

        var matches = Engine(new() { [@"C:\Work\app"] = ["pubspec.yaml"] }).Matches(tree);

        Assert.Equal(new HashSet<string> { @"C:\Work\app\build", @"C:\Work\app\.dart_tool" }, matches.Select(m => m.Path).ToHashSet());
    }

    [Fact]
    public void Should_MatchOnlyTheOutermostNodeModules_When_TheyNest()
    {
        var tree = Tree(@"C:\Work\site\node_modules\pkg\node_modules");

        var matches = Engine(new() { [@"C:\Work\site"] = ["package.json"], [@"C:\Work\site\node_modules\pkg"] = ["package.json"] }).Matches(tree);

        Assert.Equal([@"C:\Work\site\node_modules"], matches.Select(m => m.Path));
    }

    [Fact]
    public void Should_ContinueInsideKeptFolders_When_TheyHoldAWslDistribution()
    {
        var tree = Tree(@"C:\Users\dev\AppData\Local\Packages\CanonicalGroupLimited.Ubuntu_79rhkp1fndgsc\LocalState",
            @"C:\Users\dev\AppData\Local\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe");

        var matches = Engine().Matches(tree);

        Assert.Equal(["appData.packages", "wsl.ubuntu"], matches.Select(m => m.Rule.Id));
        Assert.Equal(CleanupAction.ManageWsl, matches[1].Rule.Action);
    }

    [Fact]
    public void Should_MarkStoreAppDataAsKeep_When_ItCouldNotBeRead()
    {
        var tree = FileNode.Directory(@"C:\", null,
        [
            Dir("Users", Dir("dev", Dir("AppData", Dir("Local", FileNode.Inaccessible("Packages"))))),
        ]);

        var matches = Engine().Matches(tree);

        Assert.Equal(["appData.packages"], matches.Select(m => m.Rule.Id));
    }

    [Fact]
    public void Should_RecognizeWindowsItemsAtEveryDriveRoot_When_TheyAreFilesOrFolders()
    {
        var c = FileNode.Directory(@"C:\", null,
        [
            Dir("$Recycle.Bin", Dir("S-1-5-21")),
            File("hiberfil.sys", 6_000_000_000),
            File("pagefile.sys", 4_000_000_000),
            Dir("Temp", File("hiberfil.sys", 1)),
        ]);
        var d = FileNode.Directory(@"D:\", null, [Dir("$Recycle.Bin"), Dir("Windows.old")]);

        var onC = Engine().Matches(c).Select(m => (m.Rule.Id, m.Path)).ToHashSet();
        var onD = Engine().Matches(d).Select(m => m.Rule.Id).ToHashSet();

        Assert.Equal(new HashSet<(string, string)>
        {
            ("windows.recycleBin", @"C:\$Recycle.Bin"),
            ("windows.hibernationFile", @"C:\hiberfil.sys"),
            ("windows.pageFile", @"C:\pagefile.sys"),
        }, onC);
        Assert.Equal(new HashSet<string> { "windows.recycleBin", "windows.previousInstallation" }, onD);
    }

    [Fact]
    public void Should_GiveEveryRuleAnActionThatFitsItsSafety_When_CheckingTheCatalog()
    {
        foreach (var rule in CleanupCatalog.Rules)
        {
            switch (rule.Safety)
            {
                case SafetyLevel.Safe:
                    Assert.True(rule.Action == CleanupAction.MoveToRecycleBin, rule.Id);
                    break;
                case SafetyLevel.Tool:
                    // A tool-owned item opens its tool or names the command in its reason; never the Recycle Bin.
                    Assert.True(rule.Action != CleanupAction.MoveToRecycleBin, rule.Id);
                    break;
                case SafetyLevel.Keep:
                    Assert.True(rule.Action == CleanupAction.None, rule.Id);
                    break;
            }
        }
        Assert.Equal(CleanupCatalog.Rules.Count, CleanupCatalog.Rules.Select(rule => rule.Id).Distinct().Count());
    }

    [Fact]
    public void Should_MatchEveryExactPathRule_When_ItsOwnTargetIsInTheTree()
    {
        var pathRules = CleanupCatalog.Rules.Where(rule => rule.Matcher.Kind == MatcherKind.Path).ToList();
        var tree = Tree([.. pathRules.Select(rule => Locations.Expand(rule.Matcher.Pattern))]);

        var matched = Engine().Matches(tree).Select(m => m.Rule.Id).ToHashSet();

        Assert.Equal(pathRules.Select(rule => rule.Id).ToHashSet(), matched);
    }

    [Fact]
    public void Should_NotMatchFolders_When_TheyOnlyLookLikeARulesTarget()
    {
        var tree = Tree(
            @"C:\Users\dev\.cargo\registry-backup",
            @"C:\Users\dev\AppData\Local\pip-tools",
            @"C:\Users\dev\go\pkg\sumdb",
            @"C:\Users\dev\.nuget\plugins",
            @"C:\Users\dev\AppData\Local\pnpm\global",
            @"C:\Users\dev\AppData\Local\Temporary",
            @"C:\Windows\WinSxSBackup",
            @"C:\Users\dev\AppData\Local\Google\Chrome",
            @"C:\Work\solution\.vs");

        Assert.Empty(Engine().Matches(tree));
    }

    [Fact]
    public void Should_KeepToolOwnedCachesAwayFromTheRecycleBin_When_TheirDocumentationSaysSo()
    {
        var byId = CleanupCatalog.Rules.ToDictionary(rule => rule.Id);
        foreach (var id in new[] { "dotnet.httpCache", "rust.registry", "java.maven", "python.pipCache", "node.playwright", "android.gradleCaches" })
        {
            Assert.True(byId[id].Safety == SafetyLevel.Safe, $"{id} should be safe to recycle");
        }
        // Documented as unsafe to delete by hand: uv's cache, pnpm's linked store, NuGet's expanded packages, Go's
        // read-only module cache, Rust toolchains, and Docker's disk images.
        foreach (var id in new[] { "python.uvCache", "node.pnpmStore", "dotnet.packages", "go.modCache", "rust.toolchains", "docker.data" })
        {
            Assert.True(byId[id].Safety == SafetyLevel.Tool, $"{id} should be removed with its tool");
            Assert.Equal(CleanupAction.None, byId[id].Action);
        }
    }

    [Fact]
    public void Should_MatchBuildFoldersForEachBuildSystemsMarker_When_ProjectsSitSideBySide()
    {
        var tree = Tree(
            @"C:\Work\android\build\a", @"C:\Work\kotlin\build\b", @"C:\Work\maven\target\c", @"C:\Work\crate\target\d",
            @"C:\Work\api\bin\e", @"C:\Work\api\obj\f", @"C:\Work\sln\.vs\g", @"C:\Work\game\Library\h", @"C:\Work\plain\build\i");

        var matches = Engine(new()
        {
            [@"C:\Work\android"] = ["build.gradle"],
            [@"C:\Work\kotlin"] = ["build.gradle.kts"],
            [@"C:\Work\maven"] = ["pom.xml"],
            [@"C:\Work\crate"] = ["Cargo.toml"],
            [@"C:\Work\api"] = ["Api.csproj"],
            [@"C:\Work\sln"] = ["App.sln"],
            [@"C:\Work\game"] = ["ProjectSettings"],
        }).Matches(tree);

        Assert.Equal(new HashSet<string>
        {
            "java.gradleBuild", "java.gradleBuildKts", "java.mavenTarget", "rust.target", "dotnet.bin", "dotnet.obj",
            "vs.solutionCache", "unity.library",
        }, matches.Select(m => m.Rule.Id).ToHashSet());
    }

    [Fact]
    public void Should_FindAndroidStudioCachesForEveryVersion_When_MatchingChildFolders()
    {
        var tree = Tree(@"C:\Users\dev\AppData\Local\Google\AndroidStudio2025.1", @"C:\Users\dev\AppData\Local\Google\AndroidStudioPreview2026.1");

        var matches = Engine().Matches(tree);

        Assert.Equal(2, matches.Count(m => m.Rule.Id == "android.studioCaches"));
    }

    [Fact]
    public void Should_StopPromptly_When_Cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Engine().Matches(Sample(), cancellation.Token));
    }
}

public sealed class SystemProtectionTests
{
    private static readonly SystemProtection Protection = new(KnownLocations.Sample);

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32\drivers")]
    [InlineData(@"C:\Program Files\App")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\ProgramData\Package Cache")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\dev")]
    [InlineData(@"C:\Users\someone")]
    [InlineData(@"C:\Users\dev\AppData")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\$R123.zip")]
    [InlineData(@"D:\System Volume Information")]
    [InlineData(@"C:\pagefile.sys")]
    [InlineData(@"C:\hiberfil.sys")]
    public void Should_Protect_When_WindowsNeedsTheLocation(string path)
    {
        Assert.True(Protection.IsProtected(path));
    }

    [Theory]
    [InlineData(@"C:\Users\dev\Downloads\big.iso")]
    [InlineData(@"C:\Users\dev\AppData\Local\npm-cache")]
    [InlineData(@"C:\Users\dev\source\repos\app\node_modules")]
    [InlineData(@"D:\Games\old")]
    [InlineData(@"C:\Temp\hiberfil.sys")]
    public void Should_AllowRecycling_When_ThePersonOwnsTheLocation(string path)
    {
        Assert.False(Protection.IsProtected(path));
    }
}
