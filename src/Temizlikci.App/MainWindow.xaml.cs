using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Temizlikci.App.Theme;
using Temizlikci.App.Views;
using Temizlikci.App.Views.Inspector;
using Temizlikci.App.Views.Intro;
using Temizlikci.App.Views.Overview;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;
using Temizlikci.Presentation.Updates;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Temizlikci.App;

public sealed partial class MainWindow : Window
{
    private readonly LocationServices services;
    private readonly UpdateModel updates;
    private readonly Func<LocationScanModel> makeSample;
    private bool showingIntro;
    /// <summary>Debug previews only: kept here so the timer isn't collected before it fires.</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? previewTimer;
    private readonly Dictionary<SidebarDestination, NavigationViewItem> sidebarItems = [];
    private readonly Dictionary<SidebarDestination, OverviewView> overviews = [];
    private readonly InspectorView inspector = new();
    private LocationScanModel? watchedScan;

    /// <param name="makeSample">The chart introduction's example scan, whose services never touch the disk.</param>
    public MainWindow(MainViewModel viewModel, LocationServices services, UpdateModel updates, Func<LocationScanModel> makeSample)
    {
        ViewModel = viewModel;
        this.services = services;
        this.updates = updates;
        this.makeSample = makeSample;
        InitializeComponent();
        ConfigureChrome();
        BuildSidebar();
        InspectorPane.Child = inspector;
        RestartItem.Visibility = ViewModel.Elevation.IsElevated ? Visibility.Collapsed : Visibility.Visible;
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.Undo.PropertyChanged += (_, _) => UpdateMenus();
        // Git answers arrive in the background; a project in the inspector shows them as they come.
        ViewModel.Tools.Git.PropertyChanged += (_, _) =>
        {
            if (ViewModel.Inspected is { IsProject: true }) UpdateInspector();
        };
        Activated += OnActivated;
        updates.PropertyChanged += OnUpdatesChanged;
        ShowSelection();
        ApplyPreviewArguments();
    }

    /// <summary>
    /// Debug builds only, for checking the UI by eye: <c>--folder=C:\path</c> opens that folder, <c>--scan</c> scans the
    /// open location at launch, <c>--select=name</c> picks a row by name once the scan is done, <c>--recycle=a,b</c>
    /// moves those rows to the Recycle Bin (point it only at a scratch folder), <c>--show=RecycleBin</c> then switches to
    /// that sidebar destination, and <c>--inspect-project=C:\path</c> shows that project in the inspector. <c>--return</c>
    /// comes back to the location a moment later (views are unloaded and loaded again); <c>--intro</c> opens the chart
    /// introduction.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void ApplyPreviewArguments()
    {
        var arguments = Environment.GetCommandLineArgs();
        string? Value(string name) => arguments.FirstOrDefault(argument => argument.StartsWith(name, StringComparison.Ordinal))?[name.Length..];
        if (Value("--folder=") is { } folder) ViewModel.OpenFolder(folder);
        if (arguments.Contains("--intro")) Root.Loaded += (_, _) => _ = ShowIntroAsync();
        if (!arguments.Contains("--scan") || ViewModel.CurrentScan is not { } scan) return;
        scan.StartScan();
        string? select = Value("--select=");
        var recycle = Value("--recycle=")?.Split(',').ToList() ?? [];
        var show = Enum.TryParse<DestinationKind>(Value("--show="), out var kind) ? ViewModel.InsightDestinations.FirstOrDefault(item => item.Kind == kind) : null;
        string? inspectProject = Value("--inspect-project=");
        scan.PropertyChanged += (_, _) =>
        {
            if (!scan.HasResult) return;
            if (recycle.Count > 0)
            {
                var targets = scan.Rows.Where(row => recycle.Contains(row.Node.Name)).ToList();
                recycle.Clear();
                foreach (var target in targets) scan.Recycle(target);
            }
            if (show is not null)
            {
                var destination = show;
                show = null;
                var location = ViewModel.Selection;
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.Selection = destination;
                    if (inspectProject is not null) ViewModel.Inspected = new InspectedItem(inspectProject, IsProject: true);
                    if (!arguments.Contains("--return")) return;
                    previewTimer = DispatcherQueue.CreateTimer();
                    previewTimer.Interval = TimeSpan.FromSeconds(2);
                    previewTimer.IsRepeating = false;
                    previewTimer.Tick += (_, _) => ViewModel.Selection = location;
                    previewTimer.Start();
                });
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
        // The taskbar and Alt+Tab take the window's icon, not the exe's.
        AppWindow.SetIcon(Path.Join(AppContext.BaseDirectory, "Assets", "Temizlikci.ico"));
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
        // The introduction appears once, after the first scan, when there is a real chart to relate it to.
        if (watchedScan is { HasResult: true } && !ViewModel.Settings.HasSeenChartIntro) _ = ShowIntroAsync();
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
        if (ViewModel.InsightScan is { } insightScan && ViewModel.Inspected is { } item)
        {
            if (!item.IsProject)
            {
                inspector.Show(insightScan, insightScan.NodeAnywhere(item.Id));
                return;
            }
            if (insightScan.ProjectById(item.Id) is { } project)
            {
                inspector.ShowProject(ViewModel, insightScan, project);
                return;
            }
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

    // MARK: Help: introduction, updates, about

    private async Task ShowIntroAsync()
    {
        if (showingIntro || Content.XamlRoot is null) return;
        showingIntro = true;
        ViewModel.Settings = ViewModel.Settings with { HasSeenChartIntro = true };
        try
        {
            await ChartIntroDialog.ShowAsync(Content.XamlRoot, makeSample());
        }
        finally
        {
            showingIntro = false;
        }
    }

    private async void OnHowToRead(object sender, RoutedEventArgs e) => await ShowIntroAsync();

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e) => await updates.CheckNowAsync(DateTime.UtcNow);

    private void OnDownloadUpdate(object sender, RoutedEventArgs e) => updates.Download();

    private void OnReleaseNotes(object sender, RoutedEventArgs e) => updates.OpenReleaseNotes();

    private void OnDismissUpdate(object sender, RoutedEventArgs e) => updates.Dismiss();

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        CheckForUpdatesItem.IsEnabled = !updates.IsChecking;
        UpdateBanner.IsOpen = updates.Available is not null;
        if (updates.Available is { } release)
        {
            UpdateBanner.Title = L10n.UpdatesAvailable(release.Version.ToString());
            UpdateBanner.Message = L10n.UpdatesCurrent(updates.Current.ToString());
        }
        if (e.PropertyName == nameof(UpdateModel.ManualResult) && updates.ManualResult is { } outcome) _ = ShowUpdateResultAsync(outcome);
    }

    /// <summary>The answer to Check for Updates…, which the person asked for, so it always says something.</summary>
    private async Task ShowUpdateResultAsync(ManualCheckOutcome outcome)
    {
        updates.ClearManualResult();
        if (Content.XamlRoot is null) return;
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, CloseButtonText = L10n.AlertOk, DefaultButton = ContentDialogButton.Close };
        switch (outcome)
        {
            case ManualCheckOutcome.UpToDate:
                dialog.Title = L10n.UpdatesUpToDateTitle;
                dialog.Content = L10n.UpdatesUpToDateMessage(updates.Current.ToString());
                break;
            case ManualCheckOutcome.Newer when updates.Available is { } release:
                dialog.Title = L10n.UpdatesAvailable(release.Version.ToString());
                dialog.Content = L10n.UpdatesNewerMessage(updates.Current.ToString());
                dialog.PrimaryButtonText = L10n.UpdatesDownload;
                dialog.CloseButtonText = L10n.UpdatesNotNow;
                dialog.DefaultButton = ContentDialogButton.Primary;
                break;
            default:
                dialog.Title = L10n.UpdatesFailedTitle;
                dialog.Content = L10n.UpdatesFailedMessage;
                break;
        }
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) updates.Download();
    }

    private async void OnAbout(object sender, RoutedEventArgs e)
    {
        if (Content.XamlRoot is null) return;
        var content = new StackPanel { Spacing = Ui.Double("SpacingSmall") };
        content.Children.Add(Ui.Text(L10n.AboutVersion(updates.Current.ToString()), "BodyTextStyle"));
        content.Children.Add(Ui.Wrapped(L10n.AboutSummary, "SecondaryTextStyle"));
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = L10n.AppName, Content = content, CloseButtonText = L10n.AlertOk, DefaultButton = ContentDialogButton.Close };
        await dialog.ShowAsync();
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
