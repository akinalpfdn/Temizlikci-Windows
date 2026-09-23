using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Updates;
using Temizlikci.Presentation.Intro;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Updates;
using Temizlikci.Tests.Support;

namespace Temizlikci.Tests.Presentation;

internal sealed class StubUpdates(Release? release = null, Exception? failure = null) : IUpdateChecker
{
    public int Checks { get; private set; }

    public Task<Release?> LatestReleaseAsync(CancellationToken cancellationToken)
    {
        Checks++;
        return failure is not null ? Task.FromException<Release?>(failure) : Task.FromResult(release);
    }
}

public sealed class UpdateModelTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Uri Page = new("https://github.com/akinalpfdn/Temizlikci-Windows/releases/tag/v1.2.0");
    private static readonly Uri Zip = new("https://github.com/akinalpfdn/Temizlikci-Windows/releases/download/v1.2.0/Temizlikci.zip");

    private readonly RecordingShell shell = new();
    private AppSettings settings = new();

    private static Release Version(string version) => new(AppVersion.Parse(version)!, Page, Zip);

    private UpdateModel Make(IUpdateChecker checker, string current = "1.0.0") =>
        new(AppVersion.Parse(current)!, checker, () => settings, value => settings = value, shell);

    [Fact]
    public async Task Should_ShowTheBanner_When_ANewerReleaseIsOut()
    {
        var model = Make(new StubUpdates(Version("1.2.0")));

        await model.CheckIfDueAsync(Now);

        Assert.Equal("1.2.0", model.Available?.Version.ToString());
        Assert.Equal(Now, settings.LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Should_NotAskAgain_When_TheLastCheckWasWithinADay()
    {
        var checker = new StubUpdates(Version("1.2.0"));
        settings = settings with { LastUpdateCheckUtc = Now.AddHours(-3) };

        await Make(checker).CheckIfDueAsync(Now);

        Assert.Equal(0, checker.Checks);
    }

    [Fact]
    public async Task Should_NeverCheckByItself_When_AutomaticChecksAreOff()
    {
        var checker = new StubUpdates(Version("1.2.0"));
        settings = settings with { CheckForUpdates = false };

        await Make(checker).CheckIfDueAsync(Now);

        Assert.Equal(0, checker.Checks);
    }

    [Fact]
    public async Task Should_StayQuietAboutADismissedVersion_When_CheckingAutomatically()
    {
        var model = Make(new StubUpdates(Version("1.2.0")));
        await model.CheckIfDueAsync(Now);
        model.Dismiss();

        await model.CheckIfDueAsync(Now.AddDays(2));

        Assert.Null(model.Available);
        Assert.Equal("1.2.0", settings.DismissedVersion);
    }

    [Fact]
    public async Task Should_AlwaysAnswer_When_ThePersonChecks()
    {
        var upToDate = Make(new StubUpdates(Version("1.0.0")));
        var failing = Make(new StubUpdates(failure: new HttpRequestException("offline")));
        var missing = Make(new StubUpdates(release: null));

        await upToDate.CheckNowAsync(Now);
        await failing.CheckNowAsync(Now);
        await missing.CheckNowAsync(Now);

        Assert.Equal(ManualCheckOutcome.UpToDate, upToDate.ManualResult);
        Assert.Equal(ManualCheckOutcome.Failed, failing.ManualResult);
        Assert.Equal(ManualCheckOutcome.UpToDate, missing.ManualResult);
    }

    [Fact]
    public async Task Should_SaySilentlyNothing_When_AnAutomaticCheckFails()
    {
        var model = Make(new StubUpdates(failure: new HttpRequestException("offline")));

        await model.CheckIfDueAsync(Now);

        Assert.Null(model.ManualResult);
        Assert.Null(model.Available);
        Assert.Null(settings.LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Should_OpenTheZipInTheBrowser_When_DownloadIsChosen()
    {
        var model = Make(new StubUpdates(Version("1.2.0")));
        await model.CheckNowAsync(Now);

        model.Download();
        model.OpenReleaseNotes();

        Assert.Equal([Zip, Page], shell.Opened);
    }
}

public sealed class SampleLocationTests
{
    private readonly ModelFixture fixture = new();

    [Fact]
    public async Task Should_ShowTheExampleTree_When_TheIntroScans()
    {
        var model = new LocationScanModel(SampleLocation.Location, SampleLocation.Services(fixture.Services, Temizlikci.Domain.Cleanup.KnownLocations.Sample));

        await ModelFixture.Scanned(model);

        Assert.Equal(5, model.Rows.Count);
        Assert.Equal(44_000_000_000, model.CurrentFolder!.Value.Node.AllocatedSize);
    }

    [Fact]
    public async Task Should_NeverMoveAnything_When_AskedToRecycleAnExampleItem()
    {
        var model = new LocationScanModel(SampleLocation.Location, SampleLocation.Services(fixture.Services, Temizlikci.Domain.Cleanup.KnownLocations.Sample));
        await ModelFixture.Scanned(model);

        model.Recycle(model.Rows[0]);

        Assert.Empty(fixture.Bin.Recycled);
        Assert.NotNull(model.ActionError);
        Assert.Equal(5, model.Rows.Count);
    }
}
