using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Tree;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;
using Windows.System;

namespace Temizlikci.App.Views.Growth;

/// <summary>The largest individual files in a scan, with the same actions as the chart.</summary>
internal sealed partial class LargeFilesView : UserControl
{
    private const string EmptyGlyph = "\uE8A5";

    private readonly MainViewModel main;
    private readonly LocationScanModel scan;
    private readonly StackPanel statusHost = new();
    private readonly ListView rows;
    private readonly TextBlock nameHeader = new();
    private readonly TextBlock sizeHeader = new();
    private IReadOnlyList<LargeFile>? shownFiles;
    private GrowthReport? shownGrowth;
    private SortColumn sortColumn = SortColumn.Size;
    private bool sortDescending = true;
    private bool updatingSelection;
    private bool showingDialog;

    public LargeFilesView(MainViewModel main, LocationScanModel scan)
    {
        this.main = main;
        this.scan = scan;
        rows = new ListView { SelectionMode = ListViewSelectionMode.Single, ItemContainerStyle = Ui.StretchedItems() };
        rows.SelectionChanged += OnSelectionChanged;
        rows.DoubleTapped += (_, _) => ShowSelectedInChart();
        rows.KeyDown += OnRowsKeyDown;
        rows.RightTapped += OnRightTapped;
        scan.PropertyChanged += OnScanChanged;
        main.PropertyChanged += OnMainChanged;
        Unloaded += (_, _) =>
        {
            scan.PropertyChanged -= OnScanChanged;
            main.PropertyChanged -= OnMainChanged;
        };
        Rebuild();
    }

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(shownFiles, scan.LargeFiles) || !ReferenceEquals(shownGrowth, scan.Growth)) Rebuild();
        UpdateStatus();
        if (scan.ActionError is { } error && !showingDialog && XamlRoot is not null) _ = ShowErrorAsync(error);
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Inspected)) SyncSelection();
    }

    private void Rebuild()
    {
        shownFiles = scan.LargeFiles;
        shownGrowth = scan.Growth;
        if (scan.LargeFiles.Count == 0)
        {
            Content = InsightViews.EmptyState(EmptyGlyph, L10n.LargeFilesNoneTitle, L10n.LargeFilesNoneMessage);
            return;
        }
        if (Content is not Grid)
        {
            var page = new Grid { Padding = Ui.Thickness("PagePadding"), RowSpacing = Ui.Double("SpacingSmall") };
            for (int index = 0; index < 5; index++) page.RowDefinitions.Add(new RowDefinition { Height = index == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            page.Children.Add(Ui.Text(L10n.LargeFilesTitle(scan.Location.DisplayName), "PageTitleTextStyle"));
            var hint = Ui.Text(L10n.LargeFilesHint, "SecondaryTextStyle");
            Grid.SetRow(hint, 1);
            page.Children.Add(hint);
            statusHost.Spacing = Ui.Double("SpacingSmall");
            Grid.SetRow(statusHost, 2);
            page.Children.Add(statusHost);
            var header = Header();
            Grid.SetRow(header, 3);
            page.Children.Add(header);
            Grid.SetRow(rows, 4);
            page.Children.Add(rows);
            Content = page;
        }
        UpdateHeaders();
        updatingSelection = true;
        rows.Items.Clear();
        foreach (var file in Sorted()) rows.Items.Add(Row(file));
        updatingSelection = false;
        SyncSelection();
        UpdateStatus();
    }

    private IEnumerable<LargeFile> Sorted()
    {
        var files = scan.LargeFiles;
        return (sortColumn, sortDescending) switch
        {
            (SortColumn.Name, false) => files.OrderBy(file => file.Node.Name, StringComparer.CurrentCultureIgnoreCase),
            (SortColumn.Name, true) => files.OrderByDescending(file => file.Node.Name, StringComparer.CurrentCultureIgnoreCase),
            (_, false) => files.OrderBy(file => file.Node.AllocatedSize),
            _ => files.OrderByDescending(file => file.Node.AllocatedSize),
        };
    }

    // MARK: Columns

    private Grid Header()
    {
        var header = Columns();
        header.Padding = Ui.Thickness("RowPadding");
        header.Children.Add(HeaderButton(nameHeader, SortColumn.Name, HorizontalAlignment.Left, 0));
        var modified = Ui.Text(L10n.LargeFilesModified, "BadgeTextStyle");
        modified.Foreground = Ui.Brush("TextFillColorSecondaryBrush");
        modified.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(modified, 1);
        header.Children.Add(modified);
        var change = Ui.Text(L10n.TableChange, "BadgeTextStyle");
        change.Foreground = Ui.Brush("TextFillColorSecondaryBrush");
        change.HorizontalAlignment = HorizontalAlignment.Right;
        change.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(change, 2);
        header.Children.Add(change);
        header.Children.Add(HeaderButton(sizeHeader, SortColumn.Size, HorizontalAlignment.Right, 3));
        return header;
    }

    private Button HeaderButton(TextBlock text, SortColumn column, HorizontalAlignment alignment, int gridColumn)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.Resources["ColumnHeaderButtonStyle"],
            HorizontalAlignment = alignment,
        };
        button.Click += (_, _) =>
        {
            sortDescending = sortColumn == column ? !sortDescending : column == SortColumn.Size;
            sortColumn = column;
            Rebuild();
        };
        Grid.SetColumn(button, gridColumn);
        return button;
    }

    private void UpdateHeaders()
    {
        string arrow = sortDescending ? " ↓" : " ↑";
        nameHeader.Text = L10n.TableName + (sortColumn == SortColumn.Name ? arrow : string.Empty);
        sizeHeader.Text = L10n.TableSize + (sortColumn == SortColumn.Size ? arrow : string.Empty);
    }

    private static Grid Columns()
    {
        var grid = new Grid { ColumnSpacing = Ui.Double("SpacingMedium") };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.Double("DateColumnWidth")) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = Ui.Double("GrowthColumnMinWidth") });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = Ui.Double("SizeTextMinWidth") });
        return grid;
    }

    private Grid Row(LargeFile file)
    {
        var row = Columns();
        row.Tag = file;
        var texts = new StackPanel { Spacing = Ui.Double("SpacingXXSmall") };
        var name = Ui.Text(string.Empty, "BodyTextStyle");
        MiddleTrim.SetText(name, file.Node.Name);
        texts.Children.Add(name);
        string folder = NodePath.Parent(file.Path) ?? file.Path;
        var path = Ui.Text(string.Empty, "CaptionTextStyle");
        MiddleTrim.SetText(path, folder);
        texts.Children.Add(path);
        ToolTipService.SetToolTip(row, file.Path);
        row.Children.Add(texts);

        var modified = Ui.Text(file.Node.ModifiedUtc is { } date ? Format.Date(date) : string.Empty, "SecondaryTextStyle");
        modified.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(modified, 1);
        row.Children.Add(modified);

        if (scan.GrowthFor(new NodeRef(file.Node, file.Path)) is { } change)
        {
            var pill = Ui.GrowthPill(change);
            Grid.SetColumn(pill, 2);
            row.Children.Add(pill);
        }

        var size = Ui.Text(Format.Bytes(file.Node.AllocatedSize), "SizeTextStyle");
        size.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(size, 3);
        row.Children.Add(size);
        AutomationProperties.SetName(row, $"{file.Node.Name}, {Format.Bytes(file.Node.AllocatedSize)}");
        return row;
    }

    // MARK: Selection and actions

    private LargeFile? Selected => (rows.SelectedItem as FrameworkElement)?.Tag as LargeFile;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingSelection) return;
        main.Inspected = Selected is { } file ? new InspectedItem(file.Id, IsProject: false) : null;
    }

    private void SyncSelection()
    {
        updatingSelection = true;
        rows.SelectedItem = rows.Items.OfType<FrameworkElement>().FirstOrDefault(row => row.Tag is LargeFile file && main.Inspected is { IsProject: false } item && item.Id == file.Id);
        updatingSelection = false;
    }

    private void ShowSelectedInChart()
    {
        if (Selected is { } file) main.Show(file.Path, scan);
    }

    private void OnRowsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Selected is not { } file) return;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                main.Show(file.Path, scan);
                e.Handled = true;
                break;
            case VirtualKey.Delete when scan.CanRecycle(file):
                scan.Recycle(file);
                e.Handled = true;
                break;
        }
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (FindRow(e.OriginalSource as DependencyObject) is not { Tag: LargeFile file } row) return;
        rows.SelectedItem = row;
        var node = new NodeRef(file.Node, file.Path);
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem(L10n.GrowthShowInChart, "\uE9D9", () => main.Show(file.Path, scan)));
        menu.Items.Add(MenuItem(L10n.DetailsShowInExplorer, "\uEC50", () => scan.ShowInExplorer(node)));
        menu.Items.Add(MenuItem(L10n.DetailsProperties, "\uE946", () => scan.ShowProperties(node)));
        if (scan.CanRecycle(file))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(L10n.RecycleAction, "\uE74D", () => scan.Recycle(file)));
        }
        menu.ShowAt(rows, e.GetPosition(rows));
        e.Handled = true;
    }

    /// <summary>The row a pointer event came from, walking up from whatever element was hit.</summary>
    private static Grid? FindRow(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Grid { Tag: LargeFile } row) return row;
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    private void UpdateStatus()
    {
        statusHost.Children.Clear();
        if (Ui.RecycleConfirmation(scan) is { } recycled) statusHost.Children.Add(recycled);
    }

    private async Task ShowErrorAsync(Presentation.Overview.ActionError error)
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
}
