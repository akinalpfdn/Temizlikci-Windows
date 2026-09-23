using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Scanning;

namespace Temizlikci.Presentation.Strings;

/// <summary>Turns typed failures into what people read: what happened, and what to do next.</summary>
public static class ErrorText
{
    public static (string Message, string? Suggestion) For(Exception exception) => exception switch
    {
        ScanException scan => ForScan(scan),
        RecycleException recycle => ForRecycle(recycle),
        ToolException tool => ForTool(tool),
        UnauthorizedAccessException => (L10n.ErrorNoPermission, L10n.ErrorRestartAsAdministrator),
        _ => (L10n.ErrorUnexpected(exception?.Message ?? string.Empty), null),
    };

    private static (string, string?) ForScan(ScanException exception)
    {
        string name = Name(exception.Path);
        return exception.Failure switch
        {
            ScanFailure.RootNotFound => (L10n.ScanErrorRootNotFound(name), L10n.ScanErrorChooseAnother),
            ScanFailure.RootNotFolder => (L10n.ScanErrorRootNotFolder(name), L10n.ScanErrorChooseAnother),
            _ => (L10n.ScanErrorRootUnreadable(name), L10n.ErrorRestartAsAdministrator),
        };
    }

    private static (string, string?) ForRecycle(RecycleException exception) => exception.Failure switch
    {
        RecycleFailure.NoPermission => (L10n.RecycleErrorNoPermission(exception.Name), L10n.ErrorRestartAsAdministrator),
        RecycleFailure.Missing => (L10n.RecycleErrorMissing(exception.Name), L10n.RecycleSuggestionRescan),
        RecycleFailure.InUse => (L10n.RecycleErrorInUse(exception.Name), L10n.RecycleSuggestionCloseApps),
        RecycleFailure.Protected => (L10n.RecycleErrorProtected(exception.Name), null),
        RecycleFailure.PutBackFailed => (L10n.RecycleErrorPutBack(exception.Name), L10n.RecycleSuggestionPutBack),
        RecycleFailure.Occupied => (L10n.RecycleErrorOccupied(exception.Name), L10n.RecycleSuggestionPutBack),
        _ => (L10n.RecycleErrorFailed(exception.Name), L10n.RecycleSuggestionRescan),
    };

    private static (string, string?) ForTool(ToolException exception)
    {
        string? said = exception.Detail is { Length: > 0 } detail ? L10n.ToolErrorSaid(detail) : null;
        return exception.Failure switch
        {
            ToolFailure.NotInstalled => (L10n.ToolErrorNotInstalled(exception.Tool), said),
            ToolFailure.NeedsAdministrator => (L10n.ToolErrorNeedsAdministrator(exception.Tool), L10n.ErrorRestartAsAdministrator),
            // diskpart fails mostly because something still has the disk attached; say what to close, then its words.
            _ when exception.Tool == "diskpart" => (L10n.ToolErrorFailed(exception.Tool), said is null ? L10n.ToolSuggestionWslInUse : L10n.ToolSuggestionWslInUse + "\n" + said),
            _ => (L10n.ToolErrorFailed(exception.Tool), said),
        };
    }

    private static string Name(string path)
    {
        string trimmed = path.TrimEnd('\\');
        int separator = trimmed.LastIndexOf('\\');
        return separator < 0 || trimmed.Length <= 3 ? path : trimmed[(separator + 1)..];
    }
}
