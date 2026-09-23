using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Temizlikci.App.Views.Overview;
using Temizlikci.Domain.Projects;
using Temizlikci.Domain.Tree;
using Temizlikci.Domain.Volumes;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.Inspector;

/// <summary>
/// Details and actions for one item: the selection in the chart, the open folder when nothing is selected, or the item
/// picked in an insight view. Built in code because every section is optional.
/// </summary>
internal sealed partial class InspectorView : UserControl
{
    private const string ProjectGlyph = "\uE7B8";

    private readonly StackPanel panel = new();

    public InspectorView()
    {
        panel.Padding = Thickness("InspectorPadding");
        panel.Spacing = Double("SpacingLarge");
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Shows <paramref name="node"/> from <paramref name="scan"/>, or the empty state when either is missing.</summary>
    public void Show(LocationScanModel? scan, NodeRef? node)
    {
        panel.Children.Clear();
        if (scan is null || node is not { } item)
        {
            panel.Children.Add(EmptyState());
            return;
        }
        panel.Children.Add(Header(scan, item));
        panel.Children.Add(Details(scan, item));
        if (scan.CleanupMatchFor(item) is { } match) panel.Children.Add(SafetyCard(match.Rule.Safety, RuleTexts.Reason(match.Rule.Id)));
        if (item.Node.Kind == NodeKind.Unattributed && scan.Location.IsWholeVolume) panel.Children.Add(Breakdown(scan.SpaceBreakdown));
        if (item.Node.Kind is NodeKind.Directory or NodeKind.File && scan.Identity(item) is { } identity)
        {
            panel.Children.Add(Card(L10n.IdentityTitle, IdentityTexts.Summary(identity)));
        }
        if (Explanation(item.Node.Kind) is { } explanation) panel.Children.Add(Card(null, explanation));
        if (Actions(scan, item) is { } actions) panel.Children.Add(actions);
    }

    /// <summary>A project: when it was last worked on, what it holds, and, for a Git repository, the work that exists
    /// only on this PC, so nobody deletes a project with unpushed commits by mistake.</summary>
    public void ShowProject(MainViewModel main, LocationScanModel scan, DeveloperProject project)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(project);
        main.Tools.Git.Load(project);
        panel.Children.Clear();

        var header = new Grid { ColumnSpacing = Double("SpacingMedium") };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new FontIcon { Glyph = ProjectGlyph, FontSize = Double("LargeIconSize"), Foreground = Brush("TextFillColorSecondaryBrush"), VerticalAlignment = VerticalAlignment.Top });
        var titles = new StackPanel { Spacing = Double("SpacingXXSmall") };
        var title = Text(project.Name, "SectionTitleTextStyle");
        title.IsTextSelectionEnabled = true;
        title.TextWrapping = TextWrapping.Wrap;
        titles.Children.Add(title);
        titles.Children.Add(Text(ProjectTexts.Evidence(project.Evidence), "SecondaryTextStyle"));
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        panel.Children.Add(header);

        var details = new StackPanel { Spacing = Double("SpacingSmall") };
        details.Children.Add(Row(L10n.DetailsSize, Format.Bytes(project.Node.AllocatedSize)));
        if (project.ReclaimableSize > 0) details.Children.Add(Row(L10n.ProjectsBuildOutput, Format.Bytes(project.ReclaimableSize)));
        if (project.LastTouchedUtc is { } touched) details.Children.Add(Row(L10n.ProjectsLastWorkedOn, Format.Date(touched)));
        details.Children.Add(Text(L10n.DetailsPath, "SecondaryTextStyle"));
        details.Children.Add(Text(project.Path, "PathTextStyle"));
        panel.Children.Add(details);

        if (project.Evidence == ProjectEvidence.Git) panel.Children.Add(Developer.GitViews.Card(project, main.Tools.Git));
        var note = Text(L10n.ProjectsOnlyBuildOutput, "CaptionTextStyle");
        note.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(note);

        var actions = new StackPanel { Spacing = Double("SpacingSmall") };
        var node = new NodeRef(project.Node, project.Path);
        actions.Children.Add(ActionButton(L10n.DetailsShowInExplorer, () => scan.ShowInExplorer(node)));
        foreach (var target in main.Editors(project)) actions.Children.Add(ActionButton(ProjectTexts.Editor(target.Editor), () => main.Open(target)));
        panel.Children.Add(actions);
    }

    private static StackPanel EmptyState()
    {
        var empty = new StackPanel { Style = (Style)Application.Current.Resources["EmptyStatePanelStyle"] };
        empty.Children.Add(new FontIcon { Glyph = "\uE946", Style = (Style)Application.Current.Resources["EmptyStateIconStyle"] });
        empty.Children.Add(Text(L10n.InspectorNoSelectionTitle, "EmptyStateTitleTextStyle"));
        empty.Children.Add(Text(L10n.InspectorNoSelectionMessage, "EmptyStateMessageTextStyle"));
        return empty;
    }

    private static Grid Header(LocationScanModel scan, NodeRef item)
    {
        var header = new Grid { ColumnSpacing = Double("SpacingMedium") };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new FontIcon { Glyph = Glyph(item.Node.Kind), FontSize = Double("LargeIconSize"), Foreground = Brush("TextFillColorSecondaryBrush"), VerticalAlignment = VerticalAlignment.Top });
        var titles = new StackPanel { Spacing = Double("SpacingXXSmall") };
        var title = Text(scan.Title(item), "SectionTitleTextStyle");
        title.IsTextSelectionEnabled = true;
        title.TextWrapping = TextWrapping.Wrap;
        titles.Children.Add(title);
        titles.Children.Add(Text(Kind(item.Node.Kind), "SecondaryTextStyle"));
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        return header;
    }

    private static StackPanel Details(LocationScanModel scan, NodeRef item)
    {
        var details = new StackPanel { Spacing = Double("SpacingSmall") };
        details.Children.Add(Row(L10n.DetailsSize, Format.Bytes(item.Node.AllocatedSize)));
        var root = scan.Path.Count > 0 ? scan.Path[0] : (NodeRef?)null;
        if (root is { } top && top.Id != item.Id && LocationScanModel.Share(item.Node, top.Node) is { } scanShare)
        {
            details.Children.Add(Row(L10n.DetailsShareOfScan, Format.Percent(scanShare)));
        }
        if (scan.Parent(item) is { } parent && LocationScanModel.Share(item.Node, parent.Node) is { } folderShare)
        {
            details.Children.Add(Row(L10n.DetailsShareOfFolder, Format.Percent(folderShare)));
        }
        if (scan.GrowthFor(item) is { } change && scan.Growth is { } report)
        {
            details.Children.Add(Row(L10n.GrowthSince(Format.Date(report.PreviousDateUtc)), GrowthTexts.Signed(change.Delta)));
        }
        if (item.Node.FileCount > 0) details.Children.Add(Row(L10n.DetailsFiles, Format.Count(item.Node.FileCount)));
        if (item.Node.ModifiedUtc is { } modified) details.Children.Add(Row(L10n.DetailsModified, Format.DateAndTime(modified)));
        if (LocationScanModel.ActionablePath(item) is { } path)
        {
            details.Children.Add(Text(L10n.DetailsPath, "SecondaryTextStyle"));
            details.Children.Add(Text(path, "PathTextStyle"));
        }
        return details;
    }

    private static Grid Row(string label, string value)
    {
        var row = new Grid { ColumnSpacing = Double("SpacingSmall") };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelText = Text(label, "SecondaryTextStyle");
        labelText.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(labelText);
        var valueText = Text(value, "BodyTextStyle");
        valueText.IsTextSelectionEnabled = true;
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }

    private static Border SafetyCard(Domain.Cleanup.SafetyLevel level, string reason)
    {
        var content = new StackPanel { Spacing = Double("SpacingSmall") };
        var badge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Double("SpacingXSmall") };
        badge.Children.Add(new FontIcon { Glyph = SafetyTexts.Glyph(level), FontSize = Double("CaptionFontSize"), Foreground = SafetyTexts.Ink(level) });
        var title = Text(SafetyTexts.Title(level), "BadgeTextStyle");
        title.Foreground = SafetyTexts.Ink(level);
        badge.Children.Add(title);
        content.Children.Add(badge);
        var body = Text(reason, "CaptionTextStyle");
        body.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(body);
        return Framed(content);
    }

    private static Border Breakdown(SpaceBreakdown? breakdown)
    {
        var content = new StackPanel { Spacing = Double("SpacingSmall") };
        content.Children.Add(Text(L10n.SpaceBreakdownTitle, "BodyStrongTextStyle"));
        if (breakdown is null)
        {
            content.Children.Add(new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Left });
            return Framed(content);
        }
        foreach (var part in breakdown.Parts)
        {
            content.Children.Add(Row(SpaceTexts.Title(part.Kind), Format.Bytes(part.Size)));
            var detail = Text(SpaceTexts.Detail(part.Kind, breakdown.HasUnreadableFolders), "CaptionTextStyle");
            detail.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(detail);
        }
        return Framed(content);
    }

    private static Border Card(string? title, string body)
    {
        var content = new StackPanel { Spacing = Double("SpacingSmall") };
        if (title is not null) content.Children.Add(Text(title, "BodyStrongTextStyle"));
        var text = Text(body, title is null ? "CaptionTextStyle" : "BodyTextStyle");
        text.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(text);
        return Framed(content);
    }

    private static Border Framed(UIElement content) => new()
    {
        Child = content,
        Padding = Thickness("CardPadding"),
        CornerRadius = (CornerRadius)Application.Current.Resources["MediumRadius"],
        Background = Brush("CardBackgroundFillColorSecondaryBrush"),
        BorderBrush = Brush("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
    };

    private static StackPanel? Actions(LocationScanModel scan, NodeRef item)
    {
        if (LocationScanModel.ActionablePath(item) is null) return null;
        var actions = new StackPanel { Spacing = Double("SpacingSmall") };
        actions.Children.Add(ActionButton(L10n.DetailsShowInExplorer, () => scan.ShowInExplorer(item)));
        actions.Children.Add(ActionButton(L10n.DetailsProperties, () => scan.ShowProperties(item)));
        if (scan.CanRecycle(item)) actions.Children.Add(ActionButton(L10n.RecycleAction, () => scan.Recycle(item)));
        return actions;
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => action();
        return button;
    }

    private static string Glyph(NodeKind kind) => kind switch
    {
        NodeKind.Directory => "\uE8B7",
        NodeKind.File => "\uE8A5",
        NodeKind.SmallerFiles => "\uE8F1",
        NodeKind.Inaccessible => "\uE72E",
        NodeKind.Unattributed => "\uEDA2",
        _ => "\uE916",
    };

    private static string Kind(NodeKind kind) => kind switch
    {
        NodeKind.Directory => L10n.NodeFolderKind,
        NodeKind.File => L10n.NodeFileKind,
        NodeKind.SmallerFiles => L10n.NodeSmallerFilesKind,
        NodeKind.Inaccessible => L10n.NodeProtectedKind,
        NodeKind.Unattributed => L10n.NodeUnattributedKind,
        _ => L10n.NodePendingKind,
    };

    private static string? Explanation(NodeKind kind) => kind switch
    {
        NodeKind.SmallerFiles => L10n.DetailsSmallerFilesExplanation,
        NodeKind.Inaccessible => L10n.DetailsInaccessibleExplanation,
        NodeKind.Unattributed => L10n.DetailsUnattributedExplanation,
        NodeKind.Pending => L10n.DetailsPendingExplanation,
        _ => null,
    };

    private static TextBlock Text(string text, string style) => new() { Text = text, Style = (Style)Application.Current.Resources[style] };

    private static double Double(string key) => (double)Application.Current.Resources[key];

    private static Thickness Thickness(string key) => (Thickness)Application.Current.Resources[key];

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
