using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Temizlikci.App.Theme;
using Temizlikci.App.Views.Overview;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Layout;
using Temizlikci.Domain.Tools;
using Temizlikci.Presentation.Developer;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.Developer;

/// <summary>
/// Developer artifacts the rules found, grouped by ecosystem, then WSL and Windows' own cleanup tools. Every item offers
/// the removal that fits it: the Recycle Bin for what regenerates, the owning tool for the rest, nothing for what to keep.
/// </summary>
internal sealed partial class DeveloperView : UserControl
{
    private const string WslSection = "wsl";
    private const string WindowsSection = "windows";
    private const string ProjectsSection = "projects";

    private readonly MainViewModel main;
    private readonly LocationScanModel scan;
    private readonly WindowsToolsModel tools;
    private readonly StackPanel summaryHost = new();
    private readonly StackPanel statusHost = new();
    private readonly StackPanel groupsHost = new();
    private readonly StackPanel wslHost = new();
    private readonly StackPanel windowsHost = new();
    private readonly Expander wslSection;
    private readonly Expander windowsSection;
    private readonly StaleProjectsSection staleProjects;
    private readonly HashSet<string> opened = [];
    private readonly List<ListView> lists = [];
    private IReadOnlyList<CleanupMatch>? shownMatches;
    private bool choseFirstSection;
    private bool updatingSelection;
    private bool showingDialog;

    public DeveloperView(MainViewModel main, LocationScanModel scan)
    {
        this.main = main;
        this.scan = scan;
        tools = main.Tools.Windows;
        var page = new StackPanel { Padding = Ui.Thickness("PagePadding"), Spacing = Ui.Double("SpacingMedium") };
        summaryHost.Spacing = Ui.Double("SpacingSmall");
        summaryHost.Margin = new Thickness(0, 0, 0, Ui.Double("SpacingSmall"));
        statusHost.Spacing = Ui.Double("SpacingSmall");
        groupsHost.Spacing = Ui.Double("SpacingMedium");
        wslHost.Spacing = Ui.Double("SpacingSmall");
        windowsHost.Spacing = Ui.Double("SpacingSmall");
        wslSection = Section(WslSection, L10n.WslSectionTitle, null, wslHost);
        windowsSection = Section(WindowsSection, L10n.WindowsToolsTitle, null, windowsHost);
        staleProjects = new StaleProjectsSection(main, scan);
        page.Children.Add(summaryHost);
        page.Children.Add(statusHost);
        page.Children.Add(Section(ProjectsSection, L10n.ProjectsTitle, null, staleProjects));
        page.Children.Add(groupsHost);
        page.Children.Add(wslSection);
        page.Children.Add(windowsSection);
        Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        scan.PropertyChanged += OnScanChanged;
        tools.PropertyChanged += OnToolsChanged;
        main.PropertyChanged += OnMainChanged;
        ActualThemeChanged += (_, _) => RebuildMatches();
        Loaded += async (_, _) =>
        {
            if (!tools.HasLoaded) await tools.LoadAsync();
        };
        Unloaded += (_, _) =>
        {
            scan.PropertyChanged -= OnScanChanged;
            tools.PropertyChanged -= OnToolsChanged;
            main.PropertyChanged -= OnMainChanged;
        };
        RebuildMatches();
        RebuildTools();
        UpdateStatus();
    }

    // MARK: Model changes

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A scan announces every change at once; rebuilding only when the matches changed keeps focus and scroll.
        if (!ReferenceEquals(shownMatches, scan.CleanupMatches)) RebuildMatches();
        UpdateStatus();
        if (scan.ActionError is { } error && !showingDialog && XamlRoot is not null) _ = ShowScanErrorAsync(error);
    }

    private void OnToolsChanged(object? sender, PropertyChangedEventArgs e)
    {
        RebuildTools();
        UpdateStatus();
        if (tools.Error is { } error && !showingDialog && XamlRoot is not null) _ = ShowToolErrorAsync(error);
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Inspected)) SyncSelection();
    }

    // MARK: Summary and matches

    private void RebuildMatches()
    {
        shownMatches = scan.CleanupMatches;
        RebuildSummary();
        staleProjects?.Rebuild();
        groupsHost.Children.Clear();
        lists.Clear();
        var groups = DeveloperSummary.Groups(scan.CleanupMatches);
        if (!choseFirstSection && DeveloperSummary.OpenedFirst(groups) is { } first)
        {
            choseFirstSection = true;
            opened.Add(first.ToString());
        }
        foreach (var group in groups)
        {
            string? detail = group.Reclaimable > 0 ? L10n.DeveloperReclaimable(Format.Bytes(group.Reclaimable)) : null;
            groupsHost.Children.Add(Section(group.Id, RuleTexts.Ecosystem(group.Ecosystem), detail, MatchList(group.Matches)));
        }
        SyncSelection();
    }

    private void RebuildSummary()
    {
        summaryHost.Children.Clear();
        summaryHost.Children.Add(Ui.Text(L10n.DeveloperTitle, "PageTitleTextStyle"));
        summaryHost.Children.Add(Ui.Text(L10n.DeveloperSource(scan.Location.DisplayName), "SecondaryTextStyle"));
        var totals = DeveloperSummary.Totals(scan.CleanupMatches);
        var appearance = ChartColors.Appearance(ActualTheme);
        var bar = new Grid
        {
            Height = Ui.Double("SummaryBarHeight"),
            CornerRadius = Ui.Radius("SummaryBarRadius"),
            Background = Ui.Brush("SizeBarTrackBrush"),
        };
        AutomationProperties.SetName(bar, L10n.DeveloperSummaryLabel);
        foreach (var (level, bytes) in totals.Where(total => total.Bytes > 0))
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(bytes, GridUnitType.Star) });
            var part = new Border { Background = new SolidColorBrush(ChartColors.For(SegmentFill.ForSafety(level), appearance) ?? ChartColors.Dimmed(appearance)) };
            Grid.SetColumn(part, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(part);
        }
        summaryHost.Children.Add(bar);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Double("SpacingXLarge") };
        foreach (var (level, bytes) in totals)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Double("SpacingXSmall") };
            item.Children.Add(Ui.SafetyBadge(level));
            item.Children.Add(Ui.Text(Format.Bytes(bytes), "BodyTextStyle"));
            legend.Children.Add(item);
        }
        summaryHost.Children.Add(legend);
    }

    /// <summary>A group's matches as a list: rows are selectable, reachable by keyboard, and shown in the inspector.</summary>
    private ListView MatchList(IReadOnlyList<CleanupMatch> matches)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, ItemContainerStyle = Ui.StretchedItems() };
        foreach (var match in matches) list.Items.Add(MatchRow(match));
        list.SelectionChanged += (_, _) =>
        {
            if (updatingSelection || list.SelectedItem is not FrameworkElement { Tag: CleanupMatch match }) return;
            updatingSelection = true;
            foreach (var other in lists.Where(other => !ReferenceEquals(other, list))) other.SelectedItem = null;
            updatingSelection = false;
            main.Inspected = new InspectedItem(match.Id, IsProject: false);
        };
        lists.Add(list);
        return list;
    }

    private Grid MatchRow(CleanupMatch match)
    {
        var row = new Grid { Tag = match, ColumnSpacing = Ui.Double("SpacingMedium"), Padding = Ui.Thickness("RowPadding") };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = Ui.Double("SizeTextMinWidth") });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = Ui.Double("ActionColumnMinWidth") });

        var texts = new StackPanel { Spacing = Ui.Double("SpacingXXSmall") };
        texts.Children.Add(Ui.Text(match.Node.Name, "BodyTextStyle"));
        var path = Ui.Text(string.Empty, "CaptionTextStyle");
        MiddleTrim.SetText(path, match.Path);
        ToolTipService.SetToolTip(path, match.Path);
        texts.Children.Add(path);
        texts.Children.Add(Ui.Wrapped(RuleTexts.Reason(match.Rule.Id), "CaptionTextStyle"));
        row.Children.Add(texts);

        var badge = Ui.SafetyBadge(match.Rule.Safety);
        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);

        var size = Ui.Text(Format.Bytes(match.Node.AllocatedSize), "SizeTextStyle");
        size.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(size, 2);
        row.Children.Add(size);

        if (Action(match) is { } action)
        {
            action.HorizontalAlignment = HorizontalAlignment.Right;
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 3);
            row.Children.Add(action);
        }
        AutomationProperties.SetName(row, $"{match.Node.Name}, {SafetyTexts.Title(match.Rule.Safety)}, {Format.Bytes(match.Node.AllocatedSize)}");
        return row;
    }

    private FrameworkElement? Action(CleanupMatch match) => match.Rule.Action switch
    {
        CleanupAction.MoveToRecycleBin => Ui.Button(L10n.RecycleAction, () => scan.Recycle(match)),
        CleanupAction.ManageWsl => Ui.Button(L10n.CleanupManageWsl, () => Reveal(wslSection)),
        CleanupAction.OpenAndroidStudio => AndroidStudioButton(),
        CleanupAction.OpenStorageSettings => Ui.Button(L10n.CleanupOpenStorageSettings, main.OpenStorageSettings),
        CleanupAction.CleanUpComponents => ComponentsButton(),
        CleanupAction.OpenRecycleBin => Ui.Button(L10n.RecycleOpen, main.Ledger.OpenRecycleBin),
        CleanupAction.OpenSystemProtection => Ui.Button(L10n.CleanupOpenSystemProtection, main.OpenSystemProtection, needsAdministrator: !main.Elevation.IsElevated),
        _ => null,
    };

    private void SyncSelection()
    {
        updatingSelection = true;
        foreach (var list in lists)
        {
            list.SelectedItem = list.Items.OfType<FrameworkElement>().FirstOrDefault(row => row.Tag is CleanupMatch match && main.Inspected is { IsProject: false } item && item.Id == match.Id);
        }
        updatingSelection = false;
    }

    // MARK: WSL and Windows tools

    private void RebuildTools()
    {
        RebuildWsl();
        RebuildWindows();
    }

    private void RebuildWsl()
    {
        wslHost.Children.Clear();
        long total = tools.Distributions.Sum(distribution => distribution.DiskSize ?? 0);
        SetDetail(wslSection, total > 0 ? Format.Bytes(total) : null);
        if (!tools.HasLoaded)
        {
            wslHost.Children.Add(new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Left });
            return;
        }
        if (!tools.IsWslAvailable || tools.Distributions.Count == 0)
        {
            wslHost.Children.Add(Ui.Text(tools.IsWslAvailable ? L10n.WslNone : L10n.ToolErrorNotInstalled("WSL"), "SecondaryTextStyle"));
            return;
        }
        var rows = new StackPanel();
        foreach (var distribution in tools.Distributions)
        {
            if (rows.Children.Count > 0) rows.Children.Add(Ui.Divider());
            rows.Children.Add(DistributionRow(distribution));
        }
        wslHost.Children.Add(rows);
    }

    private Grid DistributionRow(WslDistribution distribution)
    {
        var row = ToolRow();
        var texts = new StackPanel { Spacing = Ui.Double("SpacingXXSmall") };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Double("SpacingSmall") };
        title.Children.Add(Ui.Text(distribution.Name, "BodyStrongTextStyle"));
        title.Children.Add(Ui.Pill(L10n.WslVersion(distribution.Version)));
        if (distribution.IsDefault) title.Children.Add(Ui.Pill(L10n.WslDefault));
        if (distribution.IsRunning) title.Children.Add(Ui.Pill(L10n.WslRunning));
        texts.Children.Add(title);
        string location = distribution.DiskPath ?? WslOutput.PlainPath(distribution.Registration.BasePath);
        var path = Ui.Text(string.Empty, "CaptionTextStyle");
        MiddleTrim.SetText(path, location);
        ToolTipService.SetToolTip(path, location);
        texts.Children.Add(path);
        if (distribution.IsManagedByDocker) texts.Children.Add(Ui.Wrapped(L10n.WslManagedByDocker, "CaptionTextStyle"));
        row.Children.Add(texts);

        if (distribution.DiskSize is { } size)
        {
            var sizeText = Ui.Text(Format.Bytes(size), "SizeTextStyle");
            sizeText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(sizeText, 1);
            row.Children.Add(sizeText);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Double("SpacingSmall"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (distribution.CanCompact) actions.Children.Add(Ui.Button(L10n.WslCompact, () => _ = CompactAsync(distribution), needsAdministrator: !main.Elevation.IsElevated));
        if (distribution.CanUnregister) actions.Children.Add(Ui.Button(L10n.WslRemove, () => _ = RemoveAsync(distribution)));
        foreach (var button in actions.Children.OfType<Button>()) button.IsEnabled = !tools.IsWorking;
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return row;
    }

    private void RebuildWindows()
    {
        windowsHost.Children.Clear();
        var rows = new StackPanel();
        void Add(string title, string detail, FrameworkElement action)
        {
            if (rows.Children.Count > 0) rows.Children.Add(Ui.Divider());
            var row = ToolRow();
            var texts = new StackPanel { Spacing = Ui.Double("SpacingXXSmall") };
            texts.Children.Add(Ui.Text(title, "BodyStrongTextStyle"));
            texts.Children.Add(Ui.Wrapped(detail, "CaptionTextStyle"));
            row.Children.Add(texts);
            action.HorizontalAlignment = HorizontalAlignment.Right;
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 2);
            row.Children.Add(action);
            rows.Children.Add(row);
        }
        Add(L10n.ComponentsTitle, L10n.ComponentsDetail, ComponentsButton());
        Add(L10n.StorageTitle, L10n.StorageDetail, Ui.Button(L10n.CleanupOpenStorageSettings, main.OpenStorageSettings));
        Add(L10n.RestorePointsTitle, L10n.RestorePointsDetail, Ui.Button(L10n.CleanupOpenSystemProtection, main.OpenSystemProtection, needsAdministrator: !main.Elevation.IsElevated));
        Add(L10n.SidebarRecycleBin, L10n.RecycleEmptyNote, Ui.Button(L10n.RecycleOpen, main.Ledger.OpenRecycleBin));
        Add(L10n.AndroidEmulatorsTitle, L10n.AndroidEmulatorsDetail, AndroidStudioButton());
        windowsHost.Children.Add(rows);
    }

    private static Grid ToolRow()
    {
        var row = new Grid { ColumnSpacing = Ui.Double("SpacingMedium"), Padding = Ui.Thickness("RowPadding") };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = Ui.Double("SizeTextMinWidth") });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = Ui.Double("ActionColumnMinWidth") });
        return row;
    }

    private Button ComponentsButton()
    {
        var button = Ui.Button(L10n.CleanupCleanUpComponents, () => _ = CleanUpComponentsAsync(), needsAdministrator: !main.Elevation.IsElevated);
        button.IsEnabled = !tools.IsWorking;
        return button;
    }

    private FrameworkElement AndroidStudioButton() => main.IsAndroidStudioInstalled
        ? Ui.Button(L10n.CleanupOpenAndroidStudio, main.OpenAndroidStudio)
        : Ui.Text(L10n.AndroidStudioMissing, "CaptionTextStyle");

    // MARK: Status: progress, results, the Recycle Bin confirmation

    private void UpdateStatus()
    {
        statusHost.Children.Clear();
        if (tools.Working is { } working)
        {
            var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Double("SpacingSmall") };
            progress.Children.Add(new ProgressRing { IsActive = true, Width = Ui.Double("IconSize"), Height = Ui.Double("IconSize") });
            progress.Children.Add(Ui.Text(working, "SecondaryTextStyle"));
            statusHost.Children.Add(progress);
        }
        if (tools.LastResult is { } result)
        {
            var bar = new InfoBar { IsOpen = true, Severity = InfoBarSeverity.Success, Message = tools.DidChangeDisk ? $"{result} {L10n.ToolsRescanHint}" : result };
            bar.Closed += (_, _) => tools.DismissResult();
            statusHost.Children.Add(bar);
        }
        if (Ui.RecycleConfirmation(scan) is { } recycled) statusHost.Children.Add(recycled);
    }

    // MARK: Actions that ask first

    private async Task CompactAsync(WslDistribution distribution)
    {
        if (XamlRoot is null || !await Ask(L10n.WslCompactTitle(distribution.Name), L10n.WslCompactMessage(distribution.Name), L10n.WslCompactConfirm)) return;
        await tools.CompactAsync(distribution);
    }

    private async Task RemoveAsync(WslDistribution distribution)
    {
        string size = distribution.DiskSize is { } bytes ? Format.Bytes(bytes) : L10n.WslVersion(distribution.Version);
        if (XamlRoot is null || !await Ask(L10n.WslRemoveTitle(distribution.Name), L10n.WslRemoveMessage(distribution.Name, size), L10n.WslRemoveConfirm)) return;
        await tools.UnregisterAsync(distribution);
    }

    private async Task CleanUpComponentsAsync()
    {
        if (XamlRoot is null || !await Ask(L10n.ComponentsConfirmTitle, L10n.ComponentsConfirmMessage, L10n.ComponentsConfirm)) return;
        await tools.CleanUpComponentsAsync();
    }

    /// <summary>One dialog at a time: WinUI allows a single open ContentDialog per window.</summary>
    private async Task<bool> Ask(string title, string message, string confirm)
    {
        if (showingDialog) return false;
        showingDialog = true;
        try
        {
            return await Ui.ConfirmAsync(XamlRoot, title, message, confirm);
        }
        finally
        {
            showingDialog = false;
        }
    }

    private async Task ShowToolErrorAsync(ActionError error)
    {
        showingDialog = true;
        try
        {
            await Ui.ShowErrorAsync(XamlRoot, error, main.RestartAsAdministrator);
        }
        finally
        {
            showingDialog = false;
            tools.DismissError();
        }
    }

    private async Task ShowScanErrorAsync(ActionError error)
    {
        showingDialog = true;
        try
        {
            await Ui.ShowErrorAsync(XamlRoot, error, main.RestartAsAdministrator);
        }
        finally
        {
            showingDialog = false;
            scan.DismissError();
        }
    }

    // MARK: Sections

    private Expander Section(string id, string title, string? detail, UIElement content)
    {
        var section = Ui.Section(title, detail, content, opened.Contains(id));
        section.Expanding += (_, _) => opened.Add(id);
        section.Collapsed += (_, _) => opened.Remove(id);
        return section;
    }

    private static void SetDetail(Expander section, string? detail)
    {
        if (section.Header is not Grid header) return;
        if (header.Children.OfType<TextBlock>().FirstOrDefault(text => Grid.GetColumn(text) == 0) is { } title)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(section, Ui.SectionName(title.Text, detail));
        }
        var existing = header.Children.OfType<TextBlock>().FirstOrDefault(text => Grid.GetColumn(text) == 1);
        if (existing is not null) header.Children.Remove(existing);
        if (detail is null) return;
        var text = Ui.Text(detail, "SecondaryTextStyle");
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        header.Children.Add(text);
    }

    /// <summary>Opens a section and scrolls it to the top, for "Manage WSL" on a match.</summary>
    private static void Reveal(Expander section)
    {
        section.IsExpanded = true;
        section.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = true });
    }
}
