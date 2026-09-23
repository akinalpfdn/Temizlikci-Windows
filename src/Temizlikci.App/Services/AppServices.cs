using System.Text.Json;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using Temizlikci.Presentation.Main;
using Temizlikci.Services.Persistence;

namespace Temizlikci.App.Services;

/// <summary>Settings as a small JSON file in the app's folder; an unreadable file means the defaults.</summary>
internal sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string path;

    public JsonSettingsStore(string? path = null)
    {
        this.path = path ?? AppFolders.Settings;
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(path), Options) ?? new AppSettings() : new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // Settings are preferences, not data: a damaged file falls back to the defaults.
            System.Diagnostics.Trace.TraceWarning($"Reading settings failed: {exception.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            AppFolders.WriteAtomically(path, JsonSerializer.SerializeToUtf8Bytes(settings, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is better than interrupting the person; the next change tries again.
            System.Diagnostics.Trace.TraceWarning($"Saving settings failed: {exception.Message}");
        }
    }
}

/// <summary>The system folder picker, from the Windows App SDK so it also works when the app runs as administrator.</summary>
internal sealed class WindowsFolderPicker(Func<WindowId> window) : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(window())
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            CommitButtonText = Presentation.Strings.L10n.FolderPickerCommit,
        };
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }
}
