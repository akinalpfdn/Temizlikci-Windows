using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Tree;
using Temizlikci.Presentation.Developer;
using Temizlikci.Presentation.Strings;
using Temizlikci.Tests.Support;

namespace Temizlikci.Tests.Presentation;

public sealed class DeveloperSummaryTests
{
    private static CleanupMatch Match(string name, Ecosystem ecosystem, SafetyLevel safety, long size) =>
        new(new CleanupRule(name, ecosystem, safety, RuleMatcher.AtPath(name), CleanupAction.MoveToRecycleBin), FileNode.Directory(name, null, [FileNode.File("f", size, null)]), $@"C:\{name}", [$@"C:\{name}"]);

    [Fact]
    public void Should_GroupByEcosystemInDisplayOrderLargestFirst_When_Arranging()
    {
        var groups = DeveloperSummary.Groups([
            Match("small", Ecosystem.Node, SafetyLevel.Safe, 10),
            Match("wsl", Ecosystem.Wsl, SafetyLevel.Tool, 500),
            Match("big", Ecosystem.Node, SafetyLevel.Safe, 90),
        ]);

        Assert.Equal([Ecosystem.Wsl, Ecosystem.Node], groups.Select(group => group.Ecosystem));
        Assert.Equal(["big", "small"], groups[1].Matches.Select(match => match.Node.Name));
    }

    [Fact]
    public void Should_LeaveKeptItemsOutOfWhatIsReclaimable_When_Summing()
    {
        var group = Assert.Single(DeveloperSummary.Groups([
            Match("cache", Ecosystem.Android, SafetyLevel.Safe, 100),
            Match("emulators", Ecosystem.Android, SafetyLevel.Tool, 40),
            Match("keystore", Ecosystem.Android, SafetyLevel.Keep, 1_000),
        ]));

        Assert.Equal(140, group.Reclaimable);
    }

    [Fact]
    public void Should_ListEveryLevelEvenEmptyOnes_When_Totalling()
    {
        var totals = DeveloperSummary.Totals([Match("a", Ecosystem.Go, SafetyLevel.Safe, 5), Match("b", Ecosystem.Go, SafetyLevel.Safe, 7)]);

        Assert.Equal([(SafetyLevel.Safe, 12L), (SafetyLevel.Tool, 0L), (SafetyLevel.Keep, 0L)], totals);
    }

    [Fact]
    public void Should_OpenTheGroupWithTheMostSafeSpace_When_TheViewAppears()
    {
        var groups = DeveloperSummary.Groups([
            Match("docker", Ecosystem.Docker, SafetyLevel.Tool, 9_000),
            Match("target", Ecosystem.Rust, SafetyLevel.Safe, 300),
            Match("node_modules", Ecosystem.Node, SafetyLevel.Safe, 200),
        ]);

        Assert.Equal(Ecosystem.Rust, DeveloperSummary.OpenedFirst(groups));
        Assert.Null(DeveloperSummary.OpenedFirst(DeveloperSummary.Groups([Match("docker", Ecosystem.Docker, SafetyLevel.Tool, 9_000)])));
    }
}

public sealed class WindowsToolsModelTests
{
    private readonly ToolsFixture fixture = new();

    [Fact]
    public async Task Should_SayHowMuchTheDiskShrank_When_CompactingSucceeds()
    {
        var ubuntu = StubWsl.Distribution("Ubuntu", 20L << 30);
        fixture.Wsl.Installed.Add(ubuntu);
        fixture.Wsl.CompactedSize = 12L << 30;
        var model = fixture.Make().Windows;
        await model.LoadAsync();

        await model.CompactAsync(model.Distributions[0]);

        Assert.Equal(L10n.WslCompacted("Ubuntu", "12.0 GB", "8.00 GB"), model.LastResult);
        Assert.True(model.DidChangeDisk);
        Assert.False(model.IsWorking);
    }

    [Fact]
    public async Task Should_OfferARestart_When_TheToolNeedsAdministratorRights()
    {
        fixture.Wsl.Installed.Add(StubWsl.Distribution("Ubuntu", 1_000));
        fixture.Wsl.Failure = new ToolException(ToolFailure.NeedsAdministrator, "diskpart");
        var model = fixture.Make().Windows;
        await model.LoadAsync();

        await model.CompactAsync(model.Distributions[0]);

        Assert.NotNull(model.Error);
        Assert.True(model.Error.OffersRestart);
        Assert.Equal(L10n.ToolErrorNeedsAdministrator("diskpart"), model.Error.Message);
        Assert.False(model.DidChangeDisk);
        Assert.Null(model.LastResult);
    }

    [Fact]
    public async Task Should_QuoteTheTool_When_ItFails()
    {
        fixture.Wsl.Installed.Add(StubWsl.Distribution("Ubuntu", 1_000));
        fixture.Wsl.Failure = new ToolException(ToolFailure.Failed, "wsl", "The distribution is in use.");
        var model = fixture.Make().Windows;
        await model.LoadAsync();

        await model.UnregisterAsync(model.Distributions[0]);

        Assert.Equal(L10n.ToolErrorSaid("The distribution is in use."), model.Error?.Suggestion);
        Assert.False(model.Error?.OffersRestart);
        Assert.Single(model.Distributions);
    }

    [Fact]
    public async Task Should_ShowWslAsMissingWithoutAnError_When_ItIsNotInstalled()
    {
        fixture.Wsl.ListFailure = new ToolException(ToolFailure.NotInstalled, "wsl");
        var model = fixture.Make().Windows;

        await model.LoadAsync();

        Assert.False(model.IsWslAvailable);
        Assert.Null(model.Error);
        Assert.True(model.HasLoaded);
    }

    [Fact]
    public async Task Should_ReportTheSpaceDismFreed_When_ComponentCleanupFinishes()
    {
        fixture.Components.OnRun = () => fixture.Volume.Free += 3L << 30;
        var model = fixture.Make().Windows;

        await model.CleanUpComponentsAsync();

        Assert.Equal(L10n.ComponentsDone("3.00 GB"), model.LastResult);
        Assert.Equal(1, fixture.Components.Runs);
    }

    [Fact]
    public async Task Should_SayThereWasNothingToRemove_When_DismFreedNothing()
    {
        var model = fixture.Make().Windows;

        await model.CleanUpComponentsAsync();

        Assert.Equal(L10n.ComponentsDoneNothing, model.LastResult);
    }

    [Fact]
    public void Should_MapEveryToolFailureToText_When_Reporting()
    {
        foreach (var failure in Enum.GetValues<ToolFailure>())
        {
            var (message, _) = ErrorText.For(new ToolException(failure, "wsl"));
            Assert.Contains("wsl", message, StringComparison.Ordinal);
        }
        Assert.Contains(L10n.ToolSuggestionWslInUse, ErrorText.For(new ToolException(ToolFailure.Failed, "diskpart", "busy")).Suggestion, StringComparison.Ordinal);
    }
}
