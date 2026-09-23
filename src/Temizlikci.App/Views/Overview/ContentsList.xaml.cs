using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Temizlikci.Domain.Tree;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;
using Windows.System;

namespace Temizlikci.App.Views.Overview;

/// <summary>The sortable list that mirrors the chart: a conventional view of the same data, with the same colors.</summary>
public sealed partial class ContentsList : UserControl
{
    private LocationScanModel? model;
    private IReadOnlyList<NodeRef>? shownRows;
    private bool updatingSelection;
    private bool showsChanges;

    public ContentsList()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Rebuild(force: true);
        Unloaded += (_, _) => Model = null;
    }

    /// <summary>Raised when the Change column appears or goes, which moves <see cref="MinimumWidth"/>.</summary>
    public event EventHandler? MinimumWidthChanged;

    /// <summary>The width that keeps every column, with the Name column at its minimum.</summary>
    public double MinimumWidth
    {
        get
        {
            var resources = Application.Current.Resources;
            double spacing = (double)resources["SpacingSmall"];
            double columns = (double)resources["NameColumnMinWidth"] + ((GridLength)resources["SizeColumnWidth"]).Value
                + ((GridLength)resources["ShareColumnWidth"]).Value + 2 * spacing;
            if (showsChanges) columns += ((GridLength)resources["ChangeColumnWidth"]).Value + spacing;
            return columns + (double)resources["ListChromeWidth"];
        }
    }

    public LocationScanModel? Model
    {
        get => model;
        set
        {
            if (model is not null) model.PropertyChanged -= OnModelChanged;
            model = value;
            if (model is not null) model.PropertyChanged += OnModelChanged;
            Rebuild(force: true);
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Hovering the chart changes only the center text; the rows stay as they are.
        if (e.PropertyName is nameof(LocationScanModel.HoveredId) or nameof(LocationScanModel.FocusNode)) return;
        Rebuild(force: e.PropertyName is null or "" ? false : true);
    }

    /// <summary>Rebuilds the rows when the model's row list, colors or growth changed; otherwise only syncs selection.</summary>
    private void Rebuild(bool force)
    {
        if (model is null)
        {
            Rows.ItemsSource = null;
            shownRows = null;
            return;
        }
        UpdateHeaders();
        if (force || !ReferenceEquals(shownRows, model.Rows) || RowsLookStale())
        {
            shownRows = model.Rows;
            UpdateChangeColumn(model.Rows.Any(row => model.GrowthFor(row) is not null));
            var changeWidth = ChangeHeaderColumn.Width;
            long largest = model.Rows.Count == 0 ? 0 : model.Rows.Max(row => row.Node.AllocatedSize);
            Rows.ItemsSource = model.Rows.Select(row => new ContentRow(model, row, largest, ActualTheme, changeWidth)).ToList();
        }
        NoResults.Text = model.Rows.Count == 0 && model.SearchText.Length > 0 ? L10n.SearchNoResults(model.SearchText) : string.Empty;
        NoResults.Visibility = NoResults.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncSelection();
    }

    /// <summary>Colors and badges depend on highlighting, rule matches and growth, which change without new rows.</summary>
    private bool RowsLookStale()
    {
        if (Rows.ItemsSource is not List<ContentRow> rows || model is null) return true;
        return rows.Any(row => row.SwatchBrush is Microsoft.UI.Xaml.Media.SolidColorBrush brush
            && Theme.ChartColors.For(model.DisplayFill(row.Node), Theme.ChartColors.Appearance(ActualTheme)) is { } color && brush.Color != color)
            || rows.Any(row => (model.GrowthFor(row.Node) is null) != (row.Change is null))
            || rows.Any(row => (model.CleanupMatchFor(row.Node) is { } match && match.Id == row.Id) != (row.Safety is not null));
    }

    private void UpdateChangeColumn(bool show)
    {
        if (show == showsChanges) return;
        showsChanges = show;
        ChangeHeaderColumn.Width = show ? (GridLength)Application.Current.Resources["ChangeColumnWidth"] : new GridLength(0);
        ChangeHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MinimumWidthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncSelection()
    {
        if (Rows.ItemsSource is not List<ContentRow> rows || model is null) return;
        var selected = model.Selection is { } selection ? rows.FirstOrDefault(row => row.Id == selection.Id) : null;
        if (ReferenceEquals(Rows.SelectedItem, selected)) return;
        updatingSelection = true;
        Rows.SelectedItem = selected;
        if (selected is not null) Rows.ScrollIntoView(selected);
        updatingSelection = false;
    }

    private void UpdateHeaders()
    {
        if (model is null) return;
        string arrow = model.SortDescending ? " ↓" : " ↑";
        NameHeaderText.Text = L10n.TableName + (model.SortColumn == SortColumn.Name ? arrow : string.Empty);
        SizeHeaderText.Text = L10n.TableSize + (model.SortColumn == SortColumn.Size ? arrow : string.Empty);
    }

    private void OnSortByName(object sender, RoutedEventArgs e) => Sort(SortColumn.Name, defaultDescending: false);

    private void OnSortBySize(object sender, RoutedEventArgs e) => Sort(SortColumn.Size, defaultDescending: true);

    private void Sort(SortColumn column, bool defaultDescending)
    {
        if (model is null) return;
        if (model.SortColumn == column)
        {
            model.SortDescending = !model.SortDescending;
            return;
        }
        model.SortDescending = defaultDescending;
        model.SortColumn = column;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingSelection || model is null) return;
        model.SelectById((Rows.SelectedItem as ContentRow)?.Id);
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (model is not null && Rows.SelectedItem is ContentRow row) model.Open(row.Node);
    }

    private void OnRowsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (model is null || Rows.SelectedItem is not ContentRow row) return;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                model.Open(row.Node);
                e.Handled = true;
                break;
            case VirtualKey.Delete:
                model.Recycle(row.Node);
                e.Handled = true;
                break;
            case VirtualKey.Back:
                model.GoUp();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Rows drag as paths, for dropping on the sidebar's Recycle Bin. Aggregates have no path to drag.</summary>
    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<ContentRow>().Select(row => LocationScanModel.ActionablePath(row.Node)).OfType<string>().ToList();
        if (paths.Count == 0)
        {
            e.Cancel = true;
            return;
        }
        e.Data.SetData(DragPaths.Format, DragPaths.Join(paths));
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (model is null || (e.OriginalSource as FrameworkElement)?.DataContext is not ContentRow row) return;
        Rows.SelectedItem = row;
        var menu = ItemMenu.For(model, row.Node);
        if (menu.Items.Count > 0) menu.ShowAt(Rows, e.GetPosition(Rows));
        e.Handled = true;
    }
}

/// <summary>The actions for one item, shared by the list's context menu and the inspector.</summary>
internal static class ItemMenu
{
    public static MenuFlyout For(LocationScanModel model, NodeRef node)
    {
        var menu = new MenuFlyout();
        if (node.Node.Kind == NodeKind.Directory && node.Node.Children.Count > 0)
        {
            menu.Items.Add(Item(L10n.ChartOpen, "\uE838", () => model.Open(node)));
        }
        if (LocationScanModel.ActionablePath(node) is not null)
        {
            menu.Items.Add(Item(L10n.DetailsShowInExplorer, "\uEC50", () => model.ShowInExplorer(node)));
            menu.Items.Add(Item(L10n.DetailsProperties, "\uE946", () => model.ShowProperties(node)));
        }
        if (model.CanRecycle(node))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item(L10n.RecycleAction, "\uE74D", () => model.Recycle(node)));
        }
        return menu;
    }

    private static MenuFlyoutItem Item(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }
}

/// <summary>The app's own drag format: item paths, one per line.</summary>
internal static class DragPaths
{
    public const string Format = "Temizlikci.Paths";

    public static string Join(IEnumerable<string> paths) => string.Join('\n', paths);

    public static IReadOnlyList<string> Split(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
