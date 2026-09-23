using System.ComponentModel;
using System.Text;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Tools;
using Temizlikci.Services.Tools;
using Temizlikci.Tests.Presentation;
using Temizlikci.Tests.Support;

namespace Temizlikci.Tests.Services;

public sealed class WslManagerTests : IDisposable
{
    private static readonly WslRegistration Ubuntu = new("Ubuntu", @"\\?\C:\Users\me\AppData\Local\Packages\Ubuntu\LocalState", 2, "ext4.vhdx", IsDefault: true);
    private static readonly WslRegistration Docker = new("docker-desktop", @"\\?\C:\Users\me\AppData\Local\Docker\wsl\main", 2, "ext4.vhdx", IsDefault: false);
    private static readonly WslRegistration Legacy = new("Legacy", @"C:\WSL\Legacy", 1, "ext4.vhdx", IsDefault: false);

    private readonly FixtureTree scripts = new();

    public void Dispose() => scripts.Dispose();

    private WslManager Make(StubToolRunner runner, bool elevated = true, params WslRegistration[] registered) =>
        new(runner, new StubElevation(elevated), () => registered, path => path.Contains("Docker", StringComparison.Ordinal) ? 9_000 : 4_000, scripts.Root);

    /// <summary>What wsl.exe really prints: UTF-16 with a byte-order mark, CRLF lines, sometimes a trailing NUL.</summary>
    private static string Utf16(string text) => Encoding.Unicode.GetString(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray());

    [Fact]
    public async Task Should_CombineTheRegistryWithWhatRuns_When_Listing()
    {
        var runner = new StubToolRunner(_ => new ToolOutput(0, Utf16("Ubuntu\r\n\0"), string.Empty));

        var distributions = await Make(runner, true, Ubuntu, Docker, Legacy).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["docker-desktop", "Ubuntu", "Legacy"], distributions.Select(item => item.Name));
        Assert.True(distributions.Single(item => item.Name == "Ubuntu").IsRunning);
        Assert.False(distributions.Single(item => item.Name == "docker-desktop").IsRunning);
        Assert.Null(distributions.Single(item => item.Name == "Legacy").DiskSize);
        Assert.Equal(["--list", "--running", "--quiet"], runner.Commands.Single().Arguments);
        Assert.Equal(Encoding.Unicode, runner.Commands.Single().OutputEncoding);
    }

    [Fact]
    public async Task Should_TreatNothingAsRunning_When_WslAnswersWithALocalizedMessage()
    {
        var runner = new StubToolRunner(_ => new ToolOutput(1, Utf16("Çalışan dağıtım yok.\r\n"), string.Empty));

        var distributions = await Make(runner, true, Ubuntu).ListAsync(TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(distributions).IsRunning);
    }

    [Fact]
    public async Task Should_ReportWslMissing_When_ItCannotStart()
    {
        var runner = new StubToolRunner(_ => throw new Win32Exception(2));

        var error = await Assert.ThrowsAsync<ToolException>(() => Make(runner, true, Ubuntu).ListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ToolFailure.NotInstalled, error.Failure);
    }

    [Fact]
    public async Task Should_ListNothingWithoutRunningWsl_When_NoDistributionIsRegistered()
    {
        var runner = new StubToolRunner();

        Assert.Empty(await Make(runner).ListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Should_RefuseToCompact_When_TheAppIsNotElevated()
    {
        var runner = new StubToolRunner();
        var ubuntu = new WslDistribution(Ubuntu, false, 4_000);

        var error = await Assert.ThrowsAsync<ToolException>(() => Make(runner, elevated: false).CompactAsync(ubuntu, TestContext.Current.CancellationToken));

        Assert.Equal(ToolFailure.NeedsAdministrator, error.Failure);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Should_StopTheDistributionThenCompactItsDiskReadOnly_When_Compacting()
    {
        var runner = new StubToolRunner();
        var ubuntu = new WslDistribution(Ubuntu, true, 4_000);

        await Make(runner).CompactAsync(ubuntu, TestContext.Current.CancellationToken);

        Assert.Equal(["--terminate", "Ubuntu"], runner.Commands[0].Arguments);
        Assert.EndsWith("diskpart.exe", runner.Commands[1].Executable, StringComparison.OrdinalIgnoreCase);
        string script = runner.Commands[1].Arguments[1];
        Assert.Equal("/s", runner.Commands[1].Arguments[0]);
        Assert.StartsWith(scripts.Root, script, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["select vdisk file=\"C:\\Users\\me\\AppData\\Local\\Packages\\Ubuntu\\LocalState\\ext4.vhdx\"", "attach vdisk readonly", "compact vdisk", "detach vdisk", "exit"],
            File.ReadAllLines(script));
    }

    [Fact]
    public async Task Should_ReportDiskpartsLastWords_When_ItFails()
    {
        var runner = new StubToolRunner(command => command.Executable.EndsWith("diskpart.exe", StringComparison.OrdinalIgnoreCase)
            ? new ToolOutput(-2147024864, "Microsoft DiskPart\r\n\r\nThe process cannot access the file.\r\n", string.Empty)
            : new ToolOutput(0, string.Empty, string.Empty));

        var error = await Assert.ThrowsAsync<ToolException>(() => Make(runner).CompactAsync(new WslDistribution(Ubuntu, false, 4_000), TestContext.Current.CancellationToken));

        Assert.Equal(ToolFailure.Failed, error.Failure);
        Assert.Equal("diskpart", error.Tool);
        Assert.Equal("The process cannot access the file.", error.Detail);
    }

    [Fact]
    public async Task Should_NeverTouchDockersDistribution_When_AskedToCompactOrRemoveIt()
    {
        var runner = new StubToolRunner();
        var docker = new WslDistribution(Docker, true, 9_000);
        var manager = Make(runner);

        await Assert.ThrowsAsync<ToolException>(() => manager.CompactAsync(docker, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ToolException>(() => manager.UnregisterAsync(docker, TestContext.Current.CancellationToken));

        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Should_UnregisterThroughWsl_When_Removing()
    {
        var runner = new StubToolRunner();

        await Make(runner, elevated: false).UnregisterAsync(new WslDistribution(Ubuntu, false, 4_000), TestContext.Current.CancellationToken);

        Assert.Equal(["--unregister", "Ubuntu"], runner.Commands.Single().Arguments);
    }
}

public sealed class DismComponentStoreTests
{
    [Fact]
    public async Task Should_RefuseToRun_When_TheAppIsNotElevated()
    {
        var runner = new StubToolRunner();

        var error = await Assert.ThrowsAsync<ToolException>(() => new DismComponentStore(runner, new StubElevation(false)).CleanUpAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ToolFailure.NeedsAdministrator, error.Failure);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Should_RunStartComponentCleanup_When_Elevated()
    {
        var runner = new StubToolRunner();

        await new DismComponentStore(runner, new StubElevation(true)).CleanUpAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["/Online", "/Cleanup-Image", "/StartComponentCleanup"], runner.Commands.Single().Arguments);
    }

    [Theory]
    [InlineData(740, ToolFailure.NeedsAdministrator)]
    [InlineData(87, ToolFailure.Failed)]
    public async Task Should_MapDismsExitCode_When_ItFails(int exitCode, ToolFailure expected)
    {
        var runner = new StubToolRunner(_ => new ToolOutput(exitCode, "Error: 87\r\n\r\nThe parameter is incorrect.\r\n", string.Empty));

        var error = await Assert.ThrowsAsync<ToolException>(() => new DismComponentStore(runner, new StubElevation(true)).CleanUpAsync(TestContext.Current.CancellationToken));

        Assert.Equal(expected, error.Failure);
    }
}

public sealed class ProcessToolRunnerTests
{
    private static string Cmd => Path.Join(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task Should_CaptureOutputAndExitCode_When_TheToolFinishes()
    {
        var output = await new ProcessToolRunner().RunAsync(new ToolCommand(Cmd, ["/c", "echo out& echo err 1>&2& exit 3"]), TestContext.Current.CancellationToken);

        Assert.Equal(3, output.ExitCode);
        Assert.Equal("out", output.StandardOutput.Trim());
        Assert.Equal("err", output.StandardError.Trim());
    }

    [Fact]
    public async Task Should_PassArgumentsWithoutAShell_When_TheyContainSpacesAndQuotes()
    {
        string where = Path.Join(Environment.SystemDirectory, "where.exe");

        var output = await new ProcessToolRunner().RunAsync(new ToolCommand(where, ["/q", "no such tool & echo injected"]), TestContext.Current.CancellationToken);

        Assert.NotEqual(0, output.ExitCode);
        Assert.DoesNotContain("injected", output.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_StopTheToolAndItsChildren_When_Cancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProcessToolRunner().RunAsync(new ToolCommand(Cmd, ["/c", "ping -n 30 127.0.0.1 >nul"]), cancellation.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }
}
