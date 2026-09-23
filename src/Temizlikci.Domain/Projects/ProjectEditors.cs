using Temizlikci.Domain.Cleanup;

namespace Temizlikci.Domain.Projects;

public enum Editor
{
    VisualStudio,
    AndroidStudio,
    VisualStudioCode,
}

/// <summary>An app that can open a project, and what to hand it (a solution file or a folder).</summary>
public sealed record EditorTarget(Editor Editor, string Path);

/// <summary>Picks the editors that make sense for a project, most specific first.</summary>
public static class ProjectEditors
{
    public static IReadOnlyList<string> GradleMarkers { get; } = ["build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts"];

    public static IReadOnlyList<EditorTarget> Targets(DeveloperProject project, IMarkerChecker markers)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(markers);
        var targets = new List<EditorTarget>();
        // Visual Studio opens the solution in preference to a project file, so every project in it loads.
        string? visualStudio = markers.FirstMatch(project.Path, "*.sln")
            ?? markers.FirstMatch(project.Path, "*.slnx")
            ?? markers.FirstMatch(project.Path, "*.csproj");
        if (visualStudio is not null) targets.Add(new EditorTarget(Editor.VisualStudio, visualStudio));
        // Android Studio opens Gradle builds and Flutter projects from their root folder.
        bool isGradle = GradleMarkers.Any(marker => markers.Contains(project.Path, marker));
        bool isFlutter = markers.Contains(project.Path, "pubspec.yaml");
        if (isGradle || isFlutter) targets.Add(new EditorTarget(Editor.AndroidStudio, project.Path));
        targets.Add(new EditorTarget(Editor.VisualStudioCode, project.Path));
        return targets;
    }
}

/// <summary>Finds and starts editors installed on this PC.</summary>
public interface IEditorLauncher
{
    bool IsInstalled(Editor editor);

    void Open(EditorTarget target);

    /// <summary>Starts the editor without a project, e.g. Android Studio to manage emulators.</summary>
    void Start(Editor editor);
}
