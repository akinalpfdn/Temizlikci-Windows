using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Temizlikci.Domain.Actions;

namespace Temizlikci.Services.Tools;

/// <summary>Runs command-line tools as child processes: argument lists (no shell), no console window, output captured.</summary>
public sealed class ProcessToolRunner : IToolRunner
{
    static ProcessToolRunner()
    {
        // Console tools write in the OEM code page (857 for Turkish), which .NET only knows with this provider.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<ToolOutput> RunAsync(ToolCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var encoding = command.OutputEncoding ?? Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var start = new ProcessStartInfo(command.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        };
        foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new Win32Exception($"{command.Executable} didn't start.");
        using (cancellationToken.Register(() => Stop(process)))
        {
            // Both streams are read at once: a tool that fills one pipe while the other is unread would wait forever.
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            string standardOutput = await output.ConfigureAwait(false);
            string standardError = await error.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new ToolOutput(process.ExitCode, standardOutput, standardError);
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited between the cancellation and the kill: nothing left to stop.
        }
    }
}
