using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Identity;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Volumes;

namespace Temizlikci.Presentation.Strings;

/// <summary>Why each rule labels its item the way it does, from the owning tool's or Windows' own documentation.</summary>
public static class RuleTexts
{
    /// <summary>The reason for a rule; <see cref="StringCatalogTests"/>-style tests check every catalog rule has one.</summary>
    public static string Reason(string ruleId) => L10n.Get("cleanup.reason." + ruleId);

    public static string Ecosystem(Ecosystem ecosystem) => L10n.Get("cleanup.ecosystem." + ecosystem.ToString().ToLowerInvariant());
}

/// <summary>What a known folder, an app's data or a file type is, in a sentence.</summary>
public static class IdentityTexts
{
    public static string Summary(FolderIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return identity.Source switch
        {
            IdentitySource.System when identity.Folder is { } folder => L10n.Get("identity." + char.ToLowerInvariant(folder.ToString()[0]) + folder.ToString()[1..]),
            IdentitySource.App => L10n.IdentityAppData(identity.AppName ?? string.Empty),
            IdentitySource.FileType => identity.TypeName ?? string.Empty,
            _ => string.Empty,
        };
    }
}

/// <summary>The named parts of Other Used Space.</summary>
public static class SpaceTexts
{
    public static string Title(SpacePartKind kind) => kind switch
    {
        SpacePartKind.FileSystemMetadata => L10n.SpaceMetadata,
        SpacePartKind.ShadowCopies => L10n.SpaceShadowCopies,
        _ => L10n.SpaceRemainder,
    };

    public static string Detail(SpacePartKind kind, bool hasUnreadableFolders) => kind switch
    {
        SpacePartKind.FileSystemMetadata => L10n.SpaceMetadataDetail,
        SpacePartKind.ShadowCopies => L10n.SpaceShadowCopiesDetail,
        _ => hasUnreadableFolders ? L10n.SpaceRemainderDetail + " " + L10n.SpaceRemainderUnreadable : L10n.SpaceRemainderDetail,
    };
}

/// <summary>Why a folder counts as a project, and how stale periods read.</summary>
public static class ProjectTexts
{
    public static string Evidence(ProjectEvidence evidence) => L10n.Get("projects.evidence." + char.ToLowerInvariant(evidence.ToString()[0]) + evidence.ToString()[1..]);

    public static string Period(StalePeriod period) => period switch
    {
        StalePeriod.Month => L10n.ProjectsPeriodMonth,
        StalePeriod.Quarter => L10n.ProjectsPeriodQuarter,
        StalePeriod.HalfYear => L10n.ProjectsPeriodHalfYear,
        _ => L10n.ProjectsPeriodYear,
    };

    public static string Editor(Editor editor) => editor switch
    {
        Domain.Projects.Editor.VisualStudio => L10n.ProjectsOpenInVisualStudio,
        Domain.Projects.Editor.AndroidStudio => L10n.ProjectsOpenInAndroidStudio,
        _ => L10n.ProjectsOpenInVSCode,
    };
}
