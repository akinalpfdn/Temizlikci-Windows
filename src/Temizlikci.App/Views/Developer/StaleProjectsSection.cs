using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Projects;
using Temizlikci.Presentation.Developer;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.Developer;

/// <summary>
/// Projects nobody has worked on for a while, with the build output they still hold. Selecting a project shows it in
/// the inspector and opens its build output below it. Every removal is per item: there is no bulk clean.
/// </summary>
internal sealed partial class StaleProjectsSection : UserControl
{
    private const string ClosedGlyph = "\uE76C";
    private const string OpenGlyph = "\uE70D";

    private readonly MainViewModel main;
    private readonly LocationScanModel scan;
    private readonly GitStatusModel git;
    private readonly StackPanel body = new();
    private readonly ListView list;
    private bool updatingSelection;

    public StaleProjectsSection(MainViewModel main, LocationScanModel scan)
    {
        this.main = main;
        this.scan = scan;
        git = main.Tools.Git;
        body.Spacing = Ui.Double("SpacingSmall");
        body.Children.Add(PeriodPicker());
        list = new ListView { SelectionMode = ListViewSelectionMode.Single, ItemContainerStyle = Ui.StretchedItems() };
        list.SelectionChanged += OnSelectionChanged;
        Content = body;
        git.PropertyChanged += OnGitChanged;
        main.PropertyChanged += OnMainChanged;
        Unloaded += (_, _) =>
        {
            git.PropertyChanged -= OnGitChanged;
            main.PropertyChanged -= OnMainChanged;
        };
        Rebuild();
    }

    /// <summary>Projects on show now, for the section's header.</summary>
    public int Count { get; private set; }

    public void Rebuild()
    {
        var projects = scan.StaleProjects(main.Settings.StalePeriod, DateTime.UtcNow);
        Count = projects.Count;
        while (body.Children.Count > 1) body.Children.RemoveAt(1);
        if (projects.Count == 0)
        {
            body.Children.Add(Ui.Text(L10n.ProjectsEmpty, "SecondaryTextStyle"));
            return;
        }
        updatingSelection = true;
        list.Items.Clear();
        foreach (var project in projects)
        {
            git.Load(project);
            list.Items.Add(Row(project));
        }
        updatingSelection = false;
        body.Children.Add(list);
        SyncSelection();
    }

    private void OnGitChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Inspected)) SyncSelection();
        else if (e.PropertyName == nameof(MainViewModel.Settings)) Rebuild();
    }

    private ComboBox PeriodPicker()
    {
        var periods = Enum.GetValues<StalePeriod>();
        var picker = new ComboBox { Header = L10n.ProjectsPeriodLabel, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var period in periods) picker.Items.Add(ProjectTexts.Period(period));
        picker.SelectedIndex = Array.IndexOf(periods, main.Settings.StalePeriod);
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0) main.Settings = main.Settings with { StalePeriod = periods[picker.SelectedIndex] };
        };
        return picker;
    }

    private Grid Row(DeveloperProject project)
    {
        var row = new Grid { Tag = project, ColumnSpacing = Ui.Double("SpacingSmall"), RowSpacing = Ui.Double("SpacingSmall"), Padding = Ui.Thickness("RowPadding") };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // A disclosure chevron on the leading edge: the row opens to show the project's build output.
        var chevron = new FontIcon { Glyph = ClosedGlyph, FontSize = Ui.Double("CaptionFontSize"), Foreground = Ui.Brush("TextFillColorSecondaryBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Ui.Double("SpacingXSmall"), 0, 0) };
        row.Children.Add(chevron);

        var texts = new StackPanel { Spacing = Ui.Double("SpacingXXSmall") };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Double("SpacingSmall") };
        title.Children.Add(Ui.Text(project.Name, "BodyStrongTextStyle"));
        var evidence = Ui.Text(ProjectTexts.Evidence(project.Evidence), "CaptionTextStyle");
        evidence.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(evidence);
        if (GitViews.Badge(project, git) is { } badge) title.Children.Add(badge);
        texts.Children.Add(title);
        var path = Ui.Text(string.Empty, "CaptionTextStyle");
        MiddleTrim.SetText(path, project.Path);
        ToolTipService.SetToolTip(path, project.Path);
        texts.Children.Add(path);
        texts.Children.Add(Ui.Text(project.LastTouchedUtc is { } touched ? L10n.ProjectsLastTouched(Format.Date(touched)) : L10n.ProjectsUnknownActivity, "CaptionTextStyle"));
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        var sizes = new StackPanel { Spacing = Ui.Double("SpacingXXSmall"), HorizontalAlignment = HorizontalAlignment.Right };
        var total = Ui.Text(L10n.ProjectsTotal(Format.Bytes(project.Node.AllocatedSize)), "SizeTextStyle");
        sizes.Children.Add(total);
        if (project.ReclaimableSize > 0)
        {
            var reclaimable = Ui.Text(L10n.DeveloperReclaimable(Format.Bytes(project.ReclaimableSize)), "CaptionTextStyle");
            reclaimable.HorizontalAlignment = HorizontalAlignment.Right;
            sizes.Children.Add(reclaimable);
        }
        Grid.SetColumn(sizes, 2);
        row.Children.Add(sizes);

        var artifacts = Artifacts(project);
        artifacts.Visibility = Visibility.Collapsed;
        Grid.SetRow(artifacts, 1);
        Grid.SetColumn(artifacts, 1);
        Grid.SetColumnSpan(artifacts, 2);
        row.Children.Add(artifacts);
        AutomationProperties.SetName(row, $"{project.Name}, {ProjectTexts.Evidence(project.Evidence)}, {L10n.ProjectsTotal(Format.Bytes(project.Node.AllocatedSize))}");
        return row;
    }

    private StackPanel Artifacts(DeveloperProject project)
    {
        var panel = new StackPanel { Spacing = Ui.Double("SpacingXSmall") };
        if (project.Artifacts.Count == 0)
        {
            panel.Children.Add(Ui.Text(L10n.ProjectsNoArtifacts, "CaptionTextStyle"));
            return panel;
        }
        foreach (var artifact in project.Artifacts)
        {
            var line = new Grid { ColumnSpacing = Ui.Double("SpacingMedium") };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = Ui.Double("SizeTextMinWidth") });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Ui.Text(artifact.Node.Name, "BodyTextStyle");
            name.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(name);
            var folder = Ui.Text(string.Empty, "CaptionTextStyle");
            folder.VerticalAlignment = VerticalAlignment.Center;
            MiddleTrim.SetText(folder, Domain.Tree.NodePath.Parent(artifact.Path) ?? artifact.Path);
            Grid.SetColumn(folder, 1);
            line.Children.Add(folder);
            var badge = Ui.SafetyBadge(artifact.Rule.Safety);
            Grid.SetColumn(badge, 2);
            line.Children.Add(badge);
            var size = Ui.Text(Format.Bytes(artifact.Node.AllocatedSize), "SizeTextStyle");
            size.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(size, 3);
            line.Children.Add(size);
            if (artifact.Rule.Action == CleanupAction.MoveToRecycleBin && artifact.Rule.Safety == SafetyLevel.Safe)
            {
                var recycle = Ui.Button(L10n.RecycleAction, () => scan.Recycle(artifact));
                Grid.SetColumn(recycle, 4);
                line.Children.Add(recycle);
            }
            panel.Children.Add(line);
        }
        return panel;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowOpenRow();
        if (updatingSelection || (list.SelectedItem as FrameworkElement)?.Tag is not DeveloperProject project) return;
        main.Inspected = new InspectedItem(project.Id, IsProject: true);
    }

    private void SyncSelection()
    {
        updatingSelection = true;
        list.SelectedItem = list.Items.OfType<FrameworkElement>().FirstOrDefault(row => row.Tag is DeveloperProject project && main.Inspected is { IsProject: true } item && item.Id == project.Id);
        updatingSelection = false;
        ShowOpenRow();
    }

    /// <summary>The selected project's build output shows; the others fold away.</summary>
    private void ShowOpenRow()
    {
        foreach (var row in list.Items.OfType<Grid>())
        {
            bool open = ReferenceEquals(row, list.SelectedItem);
            if (row.Children.OfType<FontIcon>().FirstOrDefault() is { } chevron) chevron.Glyph = open ? OpenGlyph : ClosedGlyph;
            if (row.Children.OfType<StackPanel>().LastOrDefault() is { } artifacts) artifacts.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
