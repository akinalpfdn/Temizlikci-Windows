using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Temizlikci.Domain.Scanning;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.Overview;

/// <summary>A location's scan: empty state, progress, results (breadcrumb, chart, list), or failure.</summary>
public sealed partial class OverviewView : UserControl
{
    private const double ChartMinimumSide = 240;
    private const double ChartMaximumSide = 560;
    private const double HintHeight = 40;
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(8);

    private readonly MainViewModel main;
    private readonly DispatcherQueueTimer toastTimer;
    private Guid? shownToast;
    private bool showingError;
    private bool updatingSearch;

    public OverviewView(MainViewModel main, LocationScanModel model)
    {
        this.main = main;
        Model = model;
        InitializeComponent();
        toastTimer = DispatcherQueue.CreateTimer();
        toastTimer.Interval = ToastDuration;
        toastTimer.IsRepeating = false;
        toastTimer.Tick += (_, _) =>
        {
            if (Model.LastRecycled?.Id == shownToast) Model.DismissRecycleConfirmation();
        };
        Chart.Model = model;
        List.Model = model;
        List.MinimumWidthChanged += (_, _) => SizeChart(new Windows.Foundation.Size(Body.ActualWidth, Body.ActualHeight));
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            Model.PropertyChanged -= OnModelChanged;
            main.PropertyChanged -= OnMainChanged;
            toastTimer.Stop();
        };
    }

    public LocationScanModel Model { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Model.PropertyChanged += OnModelChanged;
        main.PropertyChanged += OnMainChanged;
        Update();
        // The saved scan comes first; a refresh starts only when it's older than the period in Settings.
        await main.OpenLocationAsync(Model, DateTime.UtcNow);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LocationScanModel.HoveredId) or nameof(LocationScanModel.FocusNode)) return;
        Update();
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsAccessBannerDismissed)) Update();
    }

    /// <summary>Moves focus to the search box (Edit › Find).</summary>
    public void FocusSearch() => SearchBox.Focus(FocusState.Keyboard);

    private void Update()
    {
        var phase = Model.Phase;
        bool loading = phase == ScanPhase.Idle && Model.CacheTask is not null;
        Results.Visibility = phase is ScanPhase.Scanning or ScanPhase.Finished ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = phase == ScanPhase.Idle && !loading ? Visibility.Visible : Visibility.Collapsed;
        LoadingState.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        FailedState.Visibility = phase == ScanPhase.Failed ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.Content = L10n.OverviewScanLocation(Model.Location.DisplayName);
        FailedMessage.Text = string.Join(' ', new[] { Model.FailureMessage, Model.FailureSuggestion }.Where(text => !string.IsNullOrEmpty(text)));

        BackButton.IsEnabled = Model.CanGoBack;
        ForwardButton.IsEnabled = Model.CanGoForward;
        Breadcrumb.ItemsSource = Model.Path.Select(Model.Title).ToList();
        RescanIcon.Glyph = Model.IsScanning ? "\uE71A" : "\uE72C";
        string rescanLabel = Model.IsScanning ? L10n.NavigationStop : L10n.NavigationRescan;
        RescanButton.Label = rescanLabel;
        ToolTipService.SetToolTip(RescanButton, rescanLabel);
        HighlightButton.IsEnabled = Model.CanHighlightReclaimable;
        HighlightButton.IsChecked = Model.IsHighlightingReclaimable;
        ToolTipService.SetToolTip(HighlightButton, Model.CanHighlightReclaimable ? L10n.MenuHighlight : L10n.MenuHighlightUnavailable);
        if (!updatingSearch && SearchBox.Text != Model.SearchText) SearchBox.Text = Model.SearchText;

        bool banner = Model.HasResult && main.ShouldShowAccessBanner(Model);
        AccessBanner.IsOpen = banner;
        AccessBanner.Visibility = banner ? Visibility.Visible : Visibility.Collapsed;
        AccessBanner.Message = L10n.AccessBannerMessage(Format.Count(Model.Result?.InaccessibleCount ?? 0));

        UpdateFooter();
        UpdateToast();
        _ = ShowErrorAsync();
    }

    private void UpdateFooter()
    {
        FooterProgress.IsActive = Model.IsRefreshing || Model.IsScanning;
        FooterProgress.Visibility = FooterProgress.IsActive ? Visibility.Visible : Visibility.Collapsed;
        RefreshLink.Visibility = Visibility.Collapsed;
        if (Model.IsScanning)
        {
            FooterText.Text = Model.Progress.Fraction is { } fraction
                ? L10n.ScanReadingTable(Format.Percent(fraction))
                : L10n.ScanProgressDetail(Format.Count(Model.Progress.FileCount), Format.Bytes(Model.Progress.AllocatedSize));
            return;
        }
        if (Model.IsRefreshing)
        {
            FooterText.Text = L10n.ScanRefreshing;
            return;
        }
        if (Model.ScannedAtUtc is not { } scannedAt)
        {
            FooterText.Text = string.Empty;
            return;
        }
        if (Model.Result is { } result)
        {
            string text = L10n.ScanScannedFooter(Format.DateAndTime(scannedAt), Durations.Describe(result.Duration));
            if (result.Method == ScanMethod.MasterFileTable) text += " · " + L10n.ScanMethodMft;
            FooterText.Text = text;
        }
        else
        {
            // The tree came from the saved scan, so there is no duration to report.
            FooterText.Text = L10n.ScanSavedFooter(Format.Relative(scannedAt, DateTime.UtcNow));
            RefreshLink.Visibility = Visibility.Visible;
        }
        if (Model.IsOutdated) FooterText.Text += " · " + L10n.ScanOutdated;
    }

    private void UpdateToast()
    {
        if (Model.LastRecycled is not { } record)
        {
            Toast.Visibility = Visibility.Collapsed;
            shownToast = null;
            return;
        }
        Toast.Visibility = Visibility.Visible;
        ToastText.Text = L10n.RecycleMoved(record.Name, Format.Bytes(record.Node.AllocatedSize));
        if (shownToast == record.Id) return;
        shownToast = record.Id;
        toastTimer.Stop();
        toastTimer.Start();
    }

    private async Task ShowErrorAsync()
    {
        if (showingError || Model.ActionError is not { } error || XamlRoot is null) return;
        showingError = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = error.Message,
                Content = error.Suggestion,
                CloseButtonText = L10n.AlertOk,
                DefaultButton = ContentDialogButton.Close,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            showingError = false;
            Model.DismissError();
        }
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs e) => SizeChart(e.NewSize);

    private void SizeChart(Windows.Foundation.Size body)
    {
        // The chart takes what's left after the list's minimum width, within its limits and the height.
        double byWidth = body.Width - List.MinimumWidth - (double)Application.Current.Resources["SpacingXLarge"];
        double side = Math.Clamp(Math.Min(byWidth, body.Height - HintHeight), ChartMinimumSide, ChartMaximumSide);
        Chart.Width = side;
        Chart.Height = side;
        ChartHint.Width = side;
    }

    private void OnBack(object sender, RoutedEventArgs e) => Model.GoBack();

    private void OnForward(object sender, RoutedEventArgs e) => Model.GoForward();

    private void OnBreadcrumbClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args) => Model.GoToAncestor(args.Index);

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        updatingSearch = true;
        Model.SearchText = sender.Text;
        updatingSearch = false;
    }

    private void OnRescanOrStop(object sender, RoutedEventArgs e)
    {
        if (Model.IsScanning) Model.StopScan();
        else Model.StartScan();
    }

    private void OnHighlight(object sender, RoutedEventArgs e) => Model.IsHighlightingReclaimable = HighlightButton.IsChecked == true;

    private void OnScan(object sender, RoutedEventArgs e) => Model.StartScan();

    private void OnRefresh(object sender, RoutedEventArgs e) => Model.StartScan(refreshing: true);

    private async void OnChooseFolder(object sender, RoutedEventArgs e) => await main.ChooseFolderAsync();

    private void OnUndoRecycle(object sender, RoutedEventArgs e)
    {
        if (Model.LastRecycled is { } record) Model.PutBack(record);
    }

    private void OnRestartAsAdministrator(object sender, RoutedEventArgs e)
    {
        if (main.RestartAsAdministrator()) Application.Current.Exit();
    }

    private void OnDismissBanner(object sender, RoutedEventArgs e) => main.IsAccessBannerDismissed = true;
}

/// <summary>"12 seconds", "3 minutes 5 seconds" — how long a scan took.</summary>
internal static class Durations
{
    public static string Describe(TimeSpan duration) => duration.TotalMinutes >= 1
        ? L10n.DurationMinutes((int)duration.TotalMinutes, duration.Seconds)
        : L10n.DurationSeconds(Math.Max(1, (int)Math.Round(duration.TotalSeconds)));
}
