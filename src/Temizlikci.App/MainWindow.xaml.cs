using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Temizlikci.App.Theme;
using Temizlikci.App.Views;
using Temizlikci.App.Views.Inspector;
using Temizlikci.App.Views.Overview;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Temizlikci.App;

public sealed partial class MainWindow : Window
{
    private readonly LocationServices services;
    private readonly Dictionary<SidebarDestination, NavigationViewItem> sidebarItems = [];
    private readonly Dictionary<SidebarDestination, OverviewView> overviews = [];
    private readonly InspectorView inspector = new();
    private LocationScanModel? watchedScan;

    public MainWindow(MainViewModel viewModel, LocationServices services)
    {
        ViewModel = viewModel;
        this.services = services;
        InitializeComponent();
        ConfigureChrome();
        BuildSidebar();
        InspectorPane.Child = inspector;
        RestartItem.Visibility = ViewModel.Elevation.IsElevated ? Visibility.Collapsed : Visibility.Visible;
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.Undo.PropertyChanged += (_, _) => UpdateMenus();
        Activated += OnActivated;
        ShowSelection();
        ApplyPreviewArguments();
    }

    /// <summary>
    /// Debug builds only, for checking the UI by eye: <c>--folder=C:\path</c> opens that folder, <c>--scan</c> scans the
    /// open location at launch, <c>--select=name</c> picks a row by name once the scan is done, <c>--recycle=a,b</c>
    /// moves those rows to the Recycle Bin (point it only at a scratch folder), <c>--show=RecycleBin</c> then switches to
    /// that sidebar destination.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void ApplyPreviewArguments()
    {
        var arguments = Environment.GetCommandLineArgs();
        string? Value(string name) => arguments.FirstOrDefault(argument => argument.StartsWith(name, StringComparison.Ordinal))?[name.Length..];
        if (Value("--folder=") is { } folder) ViewModel.OpenFolder(folder);
        if (!arguments.Contains("--scan") || ViewModel.CurrentScan is not { } scan) return;
        scan.StartScan();
        string? select = Value("--select=");
        var recycle = Value("--recycle=")?.Split(',').ToList() ?? [];
        var show = Enum.TryParse<DestinationKind>(Value("--show="), out var kind) ? ViewModel.InsightDestinations.FirstOrDefault(item => item.Kind == kind) : null;
        scan.PropertyChanged += (_, _) =>
        {
            if (!scan.HasResult) return;
            if (recycle.Count > 0)
            {
                var targets = scan.Rows.Where(row => recycle.Contains(row.Node.Name)).ToList();
                recycle.Clear();
                foreach (var target in targets) scan.Recycle(target);
                if (show is not null) DispatcherQueue.TryEnqueue(() => ViewModel.Selection = show);
            }
            if (select is not null && scan.Selection is null && scan.Rows.FirstOrDefault(row => row.Node.Name == select) is { Node: not null } row) scan.Select(row);
        };
    }

    public MainViewModel ViewModel { get; }

    private void ConfigureChrome()
    {
        Title = L10n.AppName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SystemBackdrop = new MicaBackdrop();
        WindowSizing.Apply(this);
        Root.ActualThemeChanged += (_, _) => PaintCaptionButtons();
        PaintCaptionButtons();
    }

    /// <summary>The caption buttons are drawn by the system in the system theme; this keeps them readable in the app's.</summary>
    private void PaintCaptionButtons()
    {
        var bar = AppWindow.TitleBar;
        var theme = Root.ActualTheme;
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonForegroundColor = ThemeColors.Get("CaptionForegroundColor", theme);
        bar.ButtonInactiveForegroundColor = ThemeColors.Get("CaptionInactiveForegroundColor", theme);
        bar.ButtonHoverForegroundColor = ThemeColors.Get("CaptionForegroundColor", theme);
        bar.ButtonHoverBackgroundColor = ThemeColors.Get("CaptionHoverBackgroundColor", theme);
        bar.ButtonPressedForegroundColor = ThemeColors.Get("CaptionForegroundColor", theme);
        bar.ButtonPressedBackgroundColor = ThemeColors.Get("CaptionPressedBackgroundColor", theme);
    }

    // MARK: Sidebar

    private void BuildSidebar()
    {
        Sidebar.MenuItems.Clear();
        sidebarItems.Clear();
        Sidebar.MenuItems.Add(new NavigationViewItemHeader { Content = L10n.SidebarLocations });
        foreach (var destination in ViewModel.LocationDestinations) AddSidebarItem(destination);
        var choose = new NavigationViewItem { Content = L10n.SidebarChooseFolder, Icon = new FontIcon { Glyph = "\uE710" }, SelectsOnInvoked = false };
        choose.Tapped += async (_, _) => await ViewModel.ChooseFolderAsync();
        Sidebar.MenuItems.Add(choose);
        Sidebar.MenuItems.Add(new NavigationViewItemHeader { Content = L10n.SidebarInsights });
        foreach (var destination in ViewModel.InsightDestinations) AddSidebarItem(destination);
        SelectSidebarItem();
    }

    private void AddSidebarItem(SidebarDestination destination)
    {
        var item = SidebarItems.Make(destination, ViewModel.Title(destination), ViewModel.Badge(destination));
        sidebarItems[destination] = item;
        Sidebar.MenuItems.Add(item);
        if (destination.Kind == DestinationKind.RecycleBin)
        {
            item.AllowDrop = true;
            item.DragOver += OnRecycleBinDragOver;
            item.Drop += OnRecycleBinDrop;
        }
    }

    /// <summary>Rows from the list, or files from Explorer that are part of the visible scan, can be dropped on the Recycle Bin.</summary>
    private void OnRecycleBinDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(DragPaths.Format) && !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = L10n.RecycleAction;
    }

    private async void OnRecycleBinDrop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            IReadOnlyList<string> paths = e.DataView.Contains(DragPaths.Format)
                ? DragPaths.Split((string)await e.DataView.GetDataAsync(DragPaths.Format))
                : (await e.DataView.GetStorageItemsAsync()).Select(storageItem => storageItem.Path).Where(path => path.Length > 0).ToList();
            ViewModel.RecycleDropped(paths);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void RefreshBadges()
    {
        foreach (var (destination, item) in sidebarItems) SidebarItems.Update(item, ViewModel.Title(destination), ViewModel.Badge(destination));
    }

    private void SelectSidebarItem()
    {
        if (ViewModel.Selection is { } selection && sidebarItems.TryGetValue(selection, out var item) && !ReferenceEquals(Sidebar.SelectedItem, item))
        {
            Sidebar.SelectedItem = item;
        }
    }

    private void OnSidebarSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: SidebarDestination destination }) ViewModel.Selection = destination;
    }

    // MARK: Content and inspector

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Selection):
                SelectSidebarItem();
                ShowSelection();
                break;
            case nameof(MainViewModel.LocationDestinations):
                BuildSidebar();
                break;
            case nameof(MainViewModel.Ledger):
            case nameof(MainViewModel.Drives):
                RefreshBadges();
                break;
            case nameof(MainViewModel.Subtitle):
                Subtitle.Text = ViewModel.Subtitle;
                break;
            case nameof(MainViewModel.IsInspectorPresented):
                UpdateInspectorVisibility();
                break;
            case nameof(MainViewModel.Inspected):
            case nameof(MainViewModel.Scans):
                UpdateInspector();
                break;
        }
    }

    private void ShowSelection()
    {
        ContentHost.Children.Clear();
        if (watchedScan is not null) watchedScan.PropertyChanged -= OnScanChanged;
        watchedScan = ViewModel.CurrentScan;
        if (watchedScan is not null) watchedScan.PropertyChanged += OnScanChanged;
        var selection = ViewModel.Selection;
        if (selection is null) return;
        if (selection.IsLocation && watchedScan is { } scan)
        {
            if (!overviews.TryGetValue(selection, out var overview) || !ReferenceEquals(overview.Model, scan))
            {
                overview = new OverviewView(ViewModel, scan);
                overviews[selection] = overview;
            }
            ContentHost.Children.Add(overview);
        }
        else
        {
            ContentHost.Children.Add(InsightViews.For(selection, ViewModel, services));
        }
        Subtitle.Text = ViewModel.Subtitle;
        UpdateInspector();
        UpdateMenus();
    }

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LocationScanModel.FocusNode)) return;
        UpdateInspector();
        UpdateMenus();
    }

    private void UpdateInspectorVisibility()
    {
        InspectorPane.Visibility = ViewModel.IsInspectorPresented ? Visibility.Visible : Visibility.Collapsed;
        InspectorColumn.Width = ViewModel.IsInspectorPresented ? (GridLength)Application.Current.Resources["InspectorGridWidth"] : new GridLength(0);
    }

    private void UpdateInspector()
    {
        UpdateInspectorVisibility();
        if (ViewModel.Selection?.IsLocation == true)
        {
            var scan = ViewModel.CurrentScan;
            inspector.Show(scan, scan is { Tree: not null } ? scan.Selection ?? scan.CurrentFolder : null);
            return;
        }
        if (ViewModel.InsightScan is { } insightScan && ViewModel.Inspected is { } item && !item.IsProject)
        {
            inspector.Show(insightScan, insightScan.NodeAnywhere(item.Id));
            return;
        }
        inspector.Show(null, null);
    }

    private void UpdateMenus()
    {
        var scan = ViewModel.CurrentScan;
        bool hasResult = scan?.HasResult == true;
        RescanItem.IsEnabled = scan is not null && !scan.IsScanning;
        StopItem.IsEnabled = scan?.IsScanning == true;
        ShowInExplorerItem.IsEnabled = hasResult;
        PropertiesItem.IsEnabled = hasResult;
        FindItem.IsEnabled = scan?.Tree is not null;
        RecycleItem.IsEnabled = ViewModel.CanRecycleSelection;
        UndoItem.IsEnabled = ViewModel.Undo.CanUndo;
        UndoItem.Text = ViewModel.Undo.ActionName is { } action ? L10n.MenuUndoAction(action) : L10n.MenuUndo;
        HighlightItem.IsEnabled = scan?.CanHighlightReclaimable == true;
        HighlightItem.IsChecked = scan?.IsHighlightingReclaimable == true;
        BackItem.IsEnabled = scan?.CanGoBack == true;
        ForwardItem.IsEnabled = scan?.CanGoForward == true;
        UpItem.IsEnabled = scan?.CanGoUp == true;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // Coming back from Explorer is when the Recycle Bin may have been emptied.
        if (args.WindowActivationState != WindowActivationState.Deactivated) await ViewModel.ActivatedAsync();
    }

    // MARK: Keyboard

    /// <summary>
    /// Window-wide shortcuts. Delete, Ctrl+Z and Backspace also edit text, so they only act on items while focus isn't in
    /// a text box; the rest work everywhere.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool control = IsDown(VirtualKey.Control);
        bool shift = IsDown(VirtualKey.Shift);
        bool alt = IsDown(VirtualKey.Menu);
        bool inText = FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox or AutoSuggestBox or PasswordBox;
        var scan = ViewModel.CurrentScan;
        Action? action = (e.Key, control, shift, alt) switch
        {
            (VirtualKey.O, true, false, false) => async () => await ViewModel.ChooseFolderAsync(),
            (VirtualKey.F5, false, false, false) => () => scan?.StartScan(),
            (VirtualKey.F5, false, true, false) => () => scan?.StopScan(),
            (VirtualKey.E, true, true, false) => () => scan?.ShowInExplorer(null),
            (VirtualKey.Enter, false, false, true) => () => scan?.ShowProperties(null),
            (VirtualKey.F, true, false, false) => FocusSearch,
            (VirtualKey.I, true, false, false) => () => ViewModel.IsInspectorPresented = !ViewModel.IsInspectorPresented,
            (VirtualKey.H, true, false, false) => () => ToggleHighlight(scan),
            (VirtualKey.Left, false, false, true) => () => scan?.GoBack(),
            (VirtualKey.Right, false, false, true) => () => scan?.GoForward(),
            (VirtualKey.Up, false, false, true) => () => scan?.GoUp(),
            (VirtualKey.Z, true, false, false) when !inText => ViewModel.Undo.Undo,
            (VirtualKey.Delete, false, false, false) when !inText => ViewModel.RecycleSelection,
            _ => null,
        };
        if (action is null) return;
        action();
        e.Handled = true;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void FocusSearch()
    {
        if (ViewModel.Selection is { } selection && overviews.TryGetValue(selection, out var overview)) overview.FocusSearch();
    }

    private static void ToggleHighlight(LocationScanModel? scan)
    {
        if (scan is not null) scan.IsHighlightingReclaimable = !scan.IsHighlightingReclaimable;
    }

    // MARK: Menu commands

    private async void OnChooseFolder(object sender, RoutedEventArgs e) => await ViewModel.ChooseFolderAsync();

    private void OnRescan(object sender, RoutedEventArgs e) => ViewModel.CurrentScan?.StartScan();

    private void OnStop(object sender, RoutedEventArgs e) => ViewModel.CurrentScan?.StopScan();

    private void OnShowInExplorer(object sender, RoutedEventArgs e) => ViewModel.CurrentScan?.ShowInExplorer(null);

    private void OnProperties(object sender, RoutedEventArgs e) => ViewModel.CurrentScan?.ShowProperties(null);

    private void OnRestartAsAdministrator(object sender, RoutedEventArgs e)
    {
        if (ViewModel.RestartAsAdministrator()) Close();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        ContentHost.Children.Clear();
        ContentHost.Children.Add(new Views.Settings.SettingsView(ViewModel));
        Sidebar.SelectedItem = null;
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnUndo(object sender, RoutedEventArgs e) => ViewModel.Undo.Undo();

    private void OnFind(object sender, RoutedEventArgs e) => FocusSearch();

    private void OnRecycle(object sender, RoutedEventArgs e) => ViewModel.RecycleSelection();

    private void OnHighlight(object sender, RoutedEventArgs e) => ToggleHighlight(ViewModel.CurrentScan);

    private void OnBack(object sender, RoutedEventArgs e) => ViewModel.CurrentScan?.GoBack();

    private void OnForward(object sender, RoutedEventArgs e) => ViewModel.CurrentScan?.GoForward();

    private void OnUp(object sender, RoutedEventArgs e) => ViewModel.CurrentScan?.GoUp();
}
