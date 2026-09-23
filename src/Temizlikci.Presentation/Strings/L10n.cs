using System.Globalization;
using System.Resources;

namespace Temizlikci.Presentation.Strings;

/// <summary>
/// Every user-facing string in the app. Keys and English values live in <c>Strings.resx</c>; views and view models
/// consume these members and never render string literals. Flat (no nested classes) so XAML can bind to them with
/// <c>{x:Bind s:L10n.Member}</c>. <c>StringCatalogTests</c> keeps this file and the resx in sync.
/// </summary>
public static class L10n
{
    private static readonly ResourceManager Resources = new("Temizlikci.Presentation.Strings.Strings", typeof(L10n).Assembly);

    /// <summary>A missing key shows up as ⟦key⟧ instead of crashing; <c>StringCatalogTests</c> keeps it from shipping.</summary>
    internal static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? $"⟦{key}⟧";

    internal static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    // App
    public static string AppName => Get("app.name");

    // Sidebar
    public static string SidebarLocations => Get("sidebar.section.locations");
    public static string SidebarInsights => Get("sidebar.section.insights");
    public static string SidebarLocalDiskFallback => Get("sidebar.item.localDisk");
    public static string SidebarHome => Get("sidebar.item.home");
    public static string SidebarChooseFolder => Get("sidebar.action.chooseFolder");
    public static string SidebarDeveloper => Get("sidebar.item.developer");
    public static string SidebarWhatGrew => Get("sidebar.item.whatGrew");
    public static string SidebarLargeFiles => Get("sidebar.item.largeFiles");
    public static string SidebarRecycleBin => Get("sidebar.item.recycleBin");
    public static string DriveName(string label, string letter) => Format("sidebar.item.drive", label, letter);
    public static string VolumeFree(string size) => Format("volume.free", size);

    // Menus
    public static string MenuFile => Get("menu.file");
    public static string MenuEdit => Get("menu.edit");
    public static string MenuView => Get("menu.view");
    public static string MenuGo => Get("menu.go");
    public static string MenuHelp => Get("menu.help");
    public static string MenuExit => Get("menu.file.exit");
    public static string MenuChooseFolder => Get("menu.file.chooseFolder");
    public static string MenuSettings => Get("menu.file.settings");
    public static string MenuShowInspector => Get("menu.view.inspector");

    // Inspector
    public static string InspectorNoSelectionTitle => Get("inspector.empty.title");
    public static string InspectorNoSelectionMessage => Get("inspector.empty.message");

    // Overview
    public static string OverviewEmptyTitle => Get("overview.empty.title");
    public static string OverviewEmptyMessage => Get("overview.empty.message");

    // Insights
    public static string InsightsNotScannedTitle => Get("insights.empty.title");

    // Formatting
    public static string FormatBytes(string count) => Format("format.bytes", count);
    public static string FormatOneByte => Get("format.byte");
    public static string FormatKilobytes(string value) => Format("format.kb", value);
    public static string FormatMegabytes(string value) => Format("format.mb", value);
    public static string FormatGigabytes(string value) => Format("format.gb", value);
    public static string FormatTerabytes(string value) => Format("format.tb", value);
    public static string FormatPetabytes(string value) => Format("format.pb", value);
    public static string RelativeJustNow => Get("relative.justNow");
    public static string RelativeMinutesAgo(int minutes) =>
        minutes == 1 ? Get("relative.minuteAgo") : Format("relative.minutesAgo", minutes);
    public static string RelativeHoursAgo(int hours) =>
        hours == 1 ? Get("relative.hourAgo") : Format("relative.hoursAgo", hours);
    public static string RelativeYesterday => Get("relative.yesterday");
    public static string RelativeDaysAgo(int days) => Format("relative.daysAgo", days);
    public static string RelativeOnDate(string date) => Format("relative.onDate", date);

    public static string NodeSmallerFiles(string count) => Format("node.smallerFiles", count);
    public static string NodeMergedItems => Get("node.merged");
    public static string NodeUnattributed => Get("node.unattributed");
    public static string NodePending => Get("node.pending");
    public static string NodeNeedsAccess => Get("node.needsAccess");
    public static string NodeFolderKind => Get("node.kind.folder");
    public static string NodeFileKind => Get("node.kind.file");
    public static string NodeSmallerFilesKind => Get("node.kind.smallerFiles");
    public static string NodeProtectedKind => Get("node.kind.protected");
    public static string NodeUnattributedKind => Get("node.kind.unattributed");
    public static string NodePendingKind => Get("node.kind.pending");
    public static string RecycleAction => Get("recycle.action.move");
    public static string ErrorNoPermission => Get("error.noPermission");
    public static string ErrorRestartAsAdministrator => Get("error.suggestion.admin");
    public static string ErrorUnexpected(string detail) => Format("error.unexpected", detail);
    public static string ScanErrorRootNotFound(string name) => Format("scanError.rootNotFound", name);
    public static string ScanErrorRootNotFolder(string name) => Format("scanError.rootNotFolder", name);
    public static string ScanErrorRootUnreadable(string name) => Format("scanError.rootUnreadable", name);
    public static string ScanErrorChooseAnother => Get("scanError.suggestion.chooseAnother");
    public static string RecycleErrorNoPermission(string name) => Format("recycle.error.noPermission", name);
    public static string RecycleErrorMissing(string name) => Format("recycle.error.missing", name);
    public static string RecycleErrorInUse(string name) => Format("recycle.error.inUse", name);
    public static string RecycleErrorProtected(string name) => Format("recycle.error.protected", name);
    public static string RecycleErrorFailed(string name) => Format("recycle.error.failed", name);
    public static string RecycleErrorPutBack(string name) => Format("recycle.error.putBack", name);
    public static string RecycleErrorOccupied(string name) => Format("recycle.error.occupied", name);
    public static string RecycleSuggestionRescan => Get("recycle.suggestion.rescan");
    public static string RecycleSuggestionCloseApps => Get("recycle.suggestion.closeApps");
    public static string RecycleSuggestionPutBack => Get("recycle.suggestion.putBack");
    public static string OverviewScanLocation(string name) => Format("scan.action.scanLocation", name);
    public static string ScanFailedTitle => Get("scan.failed.title");
    public static string ScanTryAgain => Get("scan.failed.tryAgain");
    public static string ScanChartHint => Get("scan.chart.hint");
    public static string ScanScannedFooter(string date, string duration) => Format("scan.footer.scanned", date, duration);
    public static string ScanSavedFooter(string age) => Format("scan.footer.saved", age);
    public static string ScanMethodMft => Get("scan.footer.mft");
    public static string ScanMeasuring => Get("scan.measuring");
    public static string ScanLoadingSaved => Get("scan.loadingSaved");
    public static string ScanRefreshing => Get("scan.refreshing");
    public static string ScanRefreshNow => Get("scan.refreshNow");
    public static string ScanSizesFooter => Get("scan.footer.sizes");
    public static string ScanOutdated => Get("scan.outdated");
    public static string ScanProgressDetail(string files, string size) => Format("scan.progress.detail", files, size);
    public static string ScanReadingTable(string percent) => Format("scan.progress.table", percent);
    public static string ChartAccessibilityLabel(string name) => Format("chart.accessibility.label", name);
    public static string ChartOpen => Get("chart.action.open");
    public static string ChartGoUp(string name) => Format("chart.center.goUp", name);
    public static string ChartUsedOfVolume(string capacity) => Format("chart.center.usedOfVolume", capacity);
    public static string ChartShareOf(string percent, string name) => Format("chart.center.shareOf", percent, name);
    public static string ChartMeasuredSoFar => Get("chart.center.measuredSoFar");
    public static string DetailsSize => Get("details.size");
    public static string DetailsShareOfScan => Get("details.shareOfScan");
    public static string DetailsShareOfFolder => Get("details.shareOfFolder");
    public static string DetailsFiles => Get("details.files");
    public static string DetailsModified => Get("details.modified");
    public static string DetailsPath => Get("details.path");
    public static string DetailsShowInExplorer => Get("details.showInExplorer");
    public static string DetailsProperties => Get("details.properties");
    public static string DetailsUnattributedExplanation => Get("details.explain.unattributed");
    public static string DetailsInaccessibleExplanation => Get("details.explain.inaccessible");
    public static string DetailsSmallerFilesExplanation => Get("details.explain.smallerFiles");
    public static string DetailsPendingExplanation => Get("details.explain.pending");
    public static string TableName => Get("table.column.name");
    public static string TableSize => Get("table.column.size");
    public static string TableShare => Get("table.column.share");
    public static string TableChange => Get("table.column.change");
    public static string NavigationBack => Get("navigation.back");
    public static string NavigationForward => Get("navigation.forward");
    public static string NavigationEnclosingFolder => Get("navigation.enclosingFolder");
    public static string NavigationRescan => Get("navigation.rescan");
    public static string NavigationStop => Get("navigation.stop");
    public static string NavigationSearchPrompt => Get("navigation.searchPrompt");
    public static string NavigationScanningSubtitle(string size) => Format("navigation.subtitle.scanning", size);
    public static string NavigationVolumeSubtitle(string used, string available) => Format("navigation.subtitle.volume", used, available);
    public static string NavigationNoScanSubtitle => Get("navigation.subtitle.noScan");
    public static string MenuFind => Get("menu.edit.find");
    public static string MenuUndo => Get("menu.edit.undo");
    public static string MenuUndoAction(string action) => Format("menu.edit.undoAction", action);
    public static string MenuShowInExplorer => Get("menu.file.showInExplorer");
    public static string MenuProperties => Get("menu.file.properties");
    public static string MenuHighlight => Get("menu.view.highlight");
    public static string MenuHighlightUnavailable => Get("menu.view.highlight.unavailable");
    public static string MenuSidebar => Get("menu.view.sidebar");
    public static string MenuHowToRead => Get("menu.help.howToRead");
    public static string MenuAbout => Get("menu.help.about");
    public static string MenuRestartAsAdministrator => Get("menu.file.restartAdmin");
    public static string AlertOk => Get("alert.ok");
    public static string AlertCancel => Get("alert.cancel");
    public static string FolderPickerCommit => Get("folderPicker.commit");

    public static string SearchNoResults(string text) => Format("search.noResults", text);
    public static string CleanupSafe => Get("cleanup.safety.safe");
    public static string CleanupTool => Get("cleanup.safety.tool");
    public static string CleanupKeep => Get("cleanup.safety.keep");
    public static string AccessBannerTitle => Get("access.banner.title");
    public static string AccessBannerMessage(string count) => Format("access.banner.message", count);
    public static string AccessRestart => Get("access.banner.restart");
    public static string AccessNotNow => Get("access.banner.notNow");
    public static string RecycleUndo => Get("recycle.action.undo");
    public static string RecycleMoved(string name, string size) => Format("recycle.toast.moved", name, size);
    public static string DurationSeconds(int seconds) => Format("duration.seconds", seconds);
    public static string DurationMinutes(int minutes, int seconds) => Format("duration.minutes", minutes, seconds);
    public static string GrowthSince(string date) => Format("growth.inspector.label", date);
    public static string IdentityTitle => Get("identity.section.title");
    public static string IdentityAppData(string name) => Format("identity.dataOf", name);
    public static string SpaceBreakdownTitle => Get("space.breakdown.title");
    public static string SpaceMetadata => Get("space.part.metadata");
    public static string SpaceMetadataDetail => Get("space.part.metadata.detail");
    public static string SpaceShadowCopies => Get("space.part.shadowCopies");
    public static string SpaceShadowCopiesDetail => Get("space.part.shadowCopies.detail");
    public static string SpaceRemainder => Get("space.part.remainder");
    public static string SpaceRemainderDetail => Get("space.part.remainder.detail");
    public static string SpaceRemainderUnreadable => Get("space.part.remainder.unreadable");
    public static string ProjectsPeriodMonth => Get("projects.period.month");
    public static string ProjectsPeriodQuarter => Get("projects.period.quarter");
    public static string ProjectsPeriodHalfYear => Get("projects.period.halfYear");
    public static string ProjectsPeriodYear => Get("projects.period.year");
    public static string ProjectsOpenInVisualStudio => Get("projects.openInVisualStudio");
    public static string ProjectsOpenInAndroidStudio => Get("projects.openInAndroidStudio");
    public static string ProjectsOpenInVSCode => Get("projects.openInVSCode");

    public static string SettingsRefreshLabel => Get("settings.refresh.label");
    public static string SettingsRefreshExplanation => Get("settings.refresh.explanation");
    public static string SettingsRefreshDay => Get("settings.refresh.day");
    public static string SettingsRefreshThreeDays => Get("settings.refresh.threeDays");
    public static string SettingsRefreshWeek => Get("settings.refresh.week");
    public static string SettingsRefreshNever => Get("settings.refresh.never");
    public static string SettingsStartAsAdministrator => Get("settings.admin");
    public static string SettingsStartAsAdministratorExplanation => Get("settings.admin.explanation");
    public static string SettingsCheckUpdates => Get("settings.checkUpdates");
    public static string SettingsCheckUpdatesExplanation => Get("settings.checkUpdates.explanation");

    public static string InsightsRecycleBinMessage => Get("insights.empty.recycleBin");
    public static string RecycleTotal(string size) => Format("recycle.summary.total", size);
    public static string RecycleEmptyNote => Get("recycle.summary.note");
    public static string RecycleFrom(string folder) => Format("recycle.row.from", folder);
    public static string RecyclePutBack => Get("recycle.action.putBack");
    public static string RecycleOpen => Get("recycle.action.open");

    public static string DeveloperTitle => Get("developer.summary.title");
    public static string DeveloperSource(string name) => Format("developer.summary.source", name);
    public static string DeveloperReclaimable(string size) => Format("developer.group.reclaimable", size);
    public static string DeveloperScanFirst => Get("developer.empty.message");
    public static string DeveloperScanHome => Get("developer.empty.scanHome");
    public static string DeveloperSummaryLabel => Get("developer.summary.bar");
    public static string CleanupManageWsl => Get("cleanup.action.manageWsl");
    public static string CleanupOpenAndroidStudio => Get("cleanup.action.openAndroidStudio");
    public static string CleanupOpenStorageSettings => Get("cleanup.action.storageSettings");
    public static string CleanupCleanUpComponents => Get("cleanup.action.components");
    public static string CleanupOpenSystemProtection => Get("cleanup.action.systemProtection");
    public static string WslSectionTitle => Get("wsl.section.title");
    public static string WslNone => Get("wsl.empty");
    public static string WslDefault => Get("wsl.badge.default");
    public static string WslRunning => Get("wsl.badge.running");
    public static string WslVersion(int version) => Format("wsl.version", version);
    public static string WslManagedByDocker => Get("wsl.docker");
    public static string WslCompact => Get("wsl.action.compact");
    public static string WslRemove => Get("wsl.action.remove");
    public static string WslCompactTitle(string name) => Format("wsl.alert.compact.title", name);
    public static string WslCompactMessage(string name) => Format("wsl.alert.compact.message", name);
    public static string WslCompactConfirm => Get("wsl.alert.compact.confirm");
    public static string WslRemoveTitle(string name) => Format("wsl.alert.remove.title", name);
    public static string WslRemoveMessage(string name, string size) => Format("wsl.alert.remove.message", name, size);
    public static string WslRemoveConfirm => Get("wsl.alert.remove.confirm");
    public static string WslCompacted(string name, string size, string saved) => Format("wsl.result.compacted", name, size, saved);
    public static string WslCompactedNothing(string name) => Format("wsl.result.compactedNothing", name);
    public static string WslRemoved(string name) => Format("wsl.result.removed", name);
    public static string WindowsToolsTitle => Get("windowsTools.section.title");
    public static string ComponentsTitle => Get("windowsTools.components.title");
    public static string ComponentsDetail => Get("windowsTools.components.detail");
    public static string ComponentsConfirmTitle => Get("windowsTools.components.alert.title");
    public static string ComponentsConfirmMessage => Get("windowsTools.components.alert.message");
    public static string ComponentsConfirm => Get("windowsTools.components.alert.confirm");
    public static string ComponentsDone(string size) => Format("windowsTools.components.done", size);
    public static string ComponentsDoneNothing => Get("windowsTools.components.doneNothing");
    public static string StorageTitle => Get("windowsTools.storage.title");
    public static string StorageDetail => Get("windowsTools.storage.detail");
    public static string RestorePointsTitle => Get("windowsTools.restorePoints.title");
    public static string RestorePointsDetail => Get("windowsTools.restorePoints.detail");
    public static string AndroidEmulatorsTitle => Get("windowsTools.android.title");
    public static string AndroidEmulatorsDetail => Get("windowsTools.android.detail");
    public static string AndroidStudioMissing => Get("windowsTools.android.missing");
    public static string ToolsWorkingComponents => Get("tools.working.components");
    public static string ToolsWorkingCompact(string name) => Format("tools.working.compact", name);
    public static string ToolsWorkingRemove(string name) => Format("tools.working.remove", name);
    public static string ToolsWorkingLoad => Get("tools.working.load");
    public static string ToolsRescanHint => Get("tools.rescanHint");
    public static string ToolsNeedsAdministrator => Get("tools.needsAdministrator");
    public static string ToolErrorNotInstalled(string tool) => Format("tools.error.notInstalled", tool);
    public static string ToolErrorNeedsAdministrator(string tool) => Format("tools.error.needsAdministrator", tool);
    public static string ToolErrorFailed(string tool) => Format("tools.error.failed", tool);
    public static string ToolErrorSaid(string detail) => Format("tools.error.said", detail);
    public static string ToolSuggestionWslInUse => Get("tools.suggestion.wslInUse");
}
