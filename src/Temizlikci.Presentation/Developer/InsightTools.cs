using Temizlikci.Domain.Actions;
using Temizlikci.Domain.Projects;

namespace Temizlikci.Presentation.Developer;

/// <summary>What the insight views hand people over to: Windows' cleanup tools, editors, Settings and Explorer.</summary>
public sealed record InsightTools(WindowsToolsModel Windows, IEditorLauncher Editors, IShell Shell);
