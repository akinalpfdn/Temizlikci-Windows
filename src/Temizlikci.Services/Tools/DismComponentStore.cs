using System.ComponentModel;
using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Tools;

namespace Temizlikci.Services.Tools;

/// <summary>Shrinks WinSxS the supported way: DISM removes components that newer updates superseded.</summary>
public sealed class DismComponentStore(IToolRunner runner, IElevation elevation) : IComponentStore
{
    private const string Dism = "DISM";
    /// <summary>ERROR_ELEVATION_REQUIRED: DISM's own answer when it isn't run as administrator.</summary>
    private const int ElevationRequired = 740;

    public async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        if (!elevation.IsElevated) throw new ToolException(ToolFailure.NeedsAdministrator, Dism);
        var command = new ToolCommand(Path.Join(Environment.SystemDirectory, "dism.exe"), ["/Online", "/Cleanup-Image", "/StartComponentCleanup"]);
        ToolOutput output;
        try
        {
            output = await runner.RunAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception exception)
        {
            throw new ToolException(ToolFailure.NotInstalled, Dism, exception.Message, exception);
        }
        if (output.ExitCode == ElevationRequired) throw new ToolException(ToolFailure.NeedsAdministrator, Dism);
        if (output.ExitCode != 0) throw new ToolException(ToolFailure.Failed, Dism, ToolText.LastWords(output));
    }
}
