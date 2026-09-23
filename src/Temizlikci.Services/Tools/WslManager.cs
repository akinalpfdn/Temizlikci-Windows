using System.ComponentModel;
using System.Text;
using Microsoft.Win32;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Tools;
using Temizlikci.Services.Persistence;

namespace Temizlikci.Services.Tools;

/// <summary>
/// WSL through its own tools. The inventory comes from the registry, which is locale-free; <c>wsl --list --verbose</c>
/// prints a localized table. Whether a distribution runs comes from <c>wsl --list --running --quiet</c>.
/// </summary>
public sealed class WslManager : IWslManager
{
    private const string Wsl = "wsl";
    private const string DiskPart = "diskpart";
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    private readonly IToolRunner runner;
    private readonly IElevation elevation;
    private readonly Func<IReadOnlyList<WslRegistration>> registrations;
    private readonly Func<string, long?> fileSize;
    private readonly string scriptFolder;

    public WslManager(IToolRunner runner, IElevation elevation)
        : this(runner, elevation, ReadRegistrations, FileSize, AppFolders.Root)
    {
    }

    /// <summary>For tests: the registry, file sizes and the script folder come from the caller.</summary>
    public WslManager(IToolRunner runner, IElevation elevation, Func<IReadOnlyList<WslRegistration>> registrations, Func<string, long?> fileSize, string scriptFolder)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
        this.registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
        this.fileSize = fileSize ?? throw new ArgumentNullException(nameof(fileSize));
        this.scriptFolder = scriptFolder ?? throw new ArgumentNullException(nameof(scriptFolder));
    }

    private static string WslExecutable => Path.Join(Environment.SystemDirectory, "wsl.exe");

    private static string DiskPartExecutable => Path.Join(Environment.SystemDirectory, "diskpart.exe");

    public async Task<IReadOnlyList<WslDistribution>> ListAsync(CancellationToken cancellationToken)
    {
        var registered = registrations();
        if (registered.Count == 0) return [];
        IReadOnlySet<string> running;
        try
        {
            var output = await runner.RunAsync(WslCommand("--list", "--running", "--quiet"), cancellationToken).ConfigureAwait(false);
            // With nothing running, wsl says so in a localized sentence and a non-zero code; either way nothing runs.
            running = output.ExitCode == 0 ? WslOutput.Names(output.StandardOutput, registered.Select(item => item.Name)) : new HashSet<string>();
        }
        catch (Win32Exception exception)
        {
            throw new ToolException(ToolFailure.NotInstalled, Wsl, exception.Message, exception);
        }
        return registered
            .Select(item =>
            {
                var distribution = new WslDistribution(item, running.Contains(item.Name), null);
                return distribution with { DiskSize = distribution.DiskPath is { } disk ? fileSize(disk) : null };
            })
            .OrderByDescending(distribution => distribution.DiskSize ?? 0)
            .ToList();
    }

    public async Task CompactAsync(WslDistribution distribution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        if (!distribution.CanCompact || distribution.DiskPath is not { } disk) throw new ToolException(ToolFailure.Failed, DiskPart);
        if (!elevation.IsElevated) throw new ToolException(ToolFailure.NeedsAdministrator, DiskPart);

        // The disk can only be compacted while nothing has it attached, so the distribution stops first.
        await Run(WslCommand("--terminate", distribution.Name), Wsl, cancellationToken).ConfigureAwait(false);

        // The script is the app's own file, rewritten each time; a fixed name means nothing accumulates.
        Directory.CreateDirectory(scriptFolder);
        string script = Path.Join(scriptFolder, "compact-vdisk.txt");
        await File.WriteAllTextAsync(script, WslOutput.CompactScript(disk), Encoding.ASCII, cancellationToken).ConfigureAwait(false);
        await Run(new ToolCommand(DiskPartExecutable, ["/s", script]), DiskPart, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnregisterAsync(WslDistribution distribution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        if (!distribution.CanUnregister) throw new ToolException(ToolFailure.Failed, Wsl);
        await Run(WslCommand("--unregister", distribution.Name), Wsl, cancellationToken).ConfigureAwait(false);
    }

    private static ToolCommand WslCommand(params string[] arguments) => new(WslExecutable, arguments) { OutputEncoding = Encoding.Unicode };

    private async Task Run(ToolCommand command, string tool, CancellationToken cancellationToken)
    {
        ToolOutput output;
        try
        {
            output = await runner.RunAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception exception)
        {
            throw new ToolException(ToolFailure.NotInstalled, tool, exception.Message, exception);
        }
        if (output.ExitCode != 0) throw new ToolException(ToolFailure.Failed, tool, ToolText.LastWords(output));
    }

    private static IReadOnlyList<WslRegistration> ReadRegistrations()
    {
        using var lxss = Registry.CurrentUser.OpenSubKey(LxssKey);
        if (lxss is null) return [];
        string? defaultId = lxss.GetValue("DefaultDistribution") as string;
        var found = new List<WslRegistration>();
        foreach (var id in lxss.GetSubKeyNames())
        {
            using var key = lxss.OpenSubKey(id);
            if (key?.GetValue("DistributionName") is not string name || key.GetValue("BasePath") is not string basePath) continue;
            int version = key.GetValue("Version") is int number ? number : 2;
            string vhd = key.GetValue("VhdFileName") as string ?? "ext4.vhdx";
            found.Add(new WslRegistration(name, basePath, version, vhd, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
        }
        return found;
    }

    private static long? FileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>What a tool said last: the line people need to see when it fails.</summary>
internal static class ToolText
{
    public static string? LastWords(ToolOutput output)
    {
        string text = (output.StandardError.Length > 0 ? output.StandardError : output.StandardOutput).Replace("\0", string.Empty, StringComparison.Ordinal);
        return text.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0);
    }
}
