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
}
