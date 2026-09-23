namespace Temizlikci.Domain.Cleanup;

public enum SafetyLevel
{
    /// <summary>Regenerates on its own; Move to Recycle Bin is fine.</summary>
    Safe,
    /// <summary>Must be removed through its owning tool so that tool's state stays consistent.</summary>
    Tool,
    /// <summary>Personal or irreplaceable data; the app never offers to delete it.</summary>
    Keep,
}

/// <summary>Groups in the Developer view, in display order.</summary>
public enum Ecosystem
{
    Windows,
    VisualStudio,
    DotNet,
    Wsl,
    Docker,
    Android,
    Flutter,
    Node,
    Rust,
    Go,
    Python,
    Java,
    Unity,
    Editors,
    AppData,
}

/// <summary>What the app offers to do with a matched item.</summary>
public enum CleanupAction
{
    MoveToRecycleBin,
    /// <summary>Jump to the WSL section, where distributions are compacted or unregistered with <c>wsl</c>.</summary>
    ManageWsl,
    OpenAndroidStudio,
    /// <summary>Settings › System › Storage, which removes temporary files, update leftovers and old installations.</summary>
    OpenStorageSettings,
    /// <summary>Run DISM's component store cleanup, the only safe way to shrink WinSxS.</summary>
    CleanUpComponents,
    OpenRecycleBin,
    /// <summary>System Protection, where restore points and their space are managed.</summary>
    OpenSystemProtection,
    /// <summary>Nothing to click: the reason names the command to run.</summary>
    None,
}

public enum MatcherKind
{
    /// <summary>An exact path; <c>~</c> and <c>%LOCALAPPDATA%</c>-style tokens expand to known locations.</summary>
    Path,
    /// <summary>Folders directly inside <see cref="RuleMatcher.Parent"/> whose names start with a prefix and contain a text.</summary>
    ChildOf,
    /// <summary>A project folder named <see cref="RuleMatcher.Name"/> next to a marker file (a glob like <c>*.csproj</c> works).</summary>
    ProjectFolder,
    /// <summary>An item with this name directly in the root of any drive (<c>$Recycle.Bin</c>, <c>hiberfil.sys</c>).</summary>
    AtDriveRoot,
}

/// <summary>How a rule recognizes an item.</summary>
public sealed record RuleMatcher(MatcherKind Kind, string Pattern, string Parent = "", string Prefix = "", string Containing = "", string Name = "", string Marker = "")
{
    public static RuleMatcher AtPath(string pattern) => new(MatcherKind.Path, pattern);

    public static RuleMatcher ChildOf(string parent, string prefix, string containing = "") =>
        new(MatcherKind.ChildOf, string.Empty, Parent: parent, Prefix: prefix, Containing: containing);

    public static RuleMatcher ProjectFolder(string name, string marker) =>
        new(MatcherKind.ProjectFolder, string.Empty, Name: name, Marker: marker);

    public static RuleMatcher AtDriveRoot(string name) => new(MatcherKind.AtDriveRoot, string.Empty, Name: name);
}

/// <summary>
/// One kind of artifact (Strategy pattern): how to find it, how safe it is and what to do. The reason people read is
/// looked up by <see cref="Id"/> in the presentation layer, so the domain carries no text.
/// </summary>
public sealed record CleanupRule(string Id, Ecosystem Ecosystem, SafetyLevel Safety, RuleMatcher Matcher, CleanupAction Action);
