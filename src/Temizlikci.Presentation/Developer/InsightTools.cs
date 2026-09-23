using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Projects;

namespace Temizlikci.Presentation.Developer;

/// <summary>What the insight views hand people over to: Windows' cleanup tools, Git, editors, Settings and Explorer.</summary>
/// <param name="Markers">Finds solution and build files in a project, to pick the editors that can open it.</param>
public sealed record InsightTools(WindowsToolsModel Windows, GitStatusModel Git, IEditorLauncher Editors, IMarkerChecker Markers, IShell Shell);
