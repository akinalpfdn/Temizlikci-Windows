using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Temizlikci.App.Theme;
using Temizlikci.App.Views;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App;

public sealed partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ConfigureChrome();
        BuildSidebar();
        ViewModel.PropertyChanged += OnViewModelChanged;
        ShowSelection();
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
        ApplyPreviewTheme();
        Root.ActualThemeChanged += (_, _) => PaintCaptionButtons();
        PaintCaptionButtons();
    }

    /// <summary>The caption buttons are drawn by the system and follow the system theme, not the window's; this keeps
    /// them readable when the two differ.</summary>
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

    /// <summary>
    /// Debug builds only: <c>--theme=light</c> or <c>--theme=dark</c> forces a theme so both can be checked without
    /// changing the system setting.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void ApplyPreviewTheme()
    {
        string? argument = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith("--theme=", StringComparison.Ordinal));
        if (argument is null) return;
        Root.RequestedTheme = argument.EndsWith("dark", StringComparison.Ordinal) ? ElementTheme.Dark : ElementTheme.Light;
    }

    private void BuildSidebar()
    {
        Sidebar.MenuItems.Add(new NavigationViewItemHeader { Content = L10n.SidebarLocations });
        foreach (var destination in ViewModel.LocationDestinations)
        {
            Sidebar.MenuItems.Add(SidebarItems.Make(destination, ViewModel.Title(destination), ViewModel.Badge(destination)));
        }
        Sidebar.MenuItems.Add(new NavigationViewItemHeader { Content = L10n.SidebarInsights });
        foreach (var destination in ViewModel.InsightDestinations)
        {
            Sidebar.MenuItems.Add(SidebarItems.Make(destination, ViewModel.Title(destination), ViewModel.Badge(destination)));
        }
        Sidebar.SelectedItem = Sidebar.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => Equals(item.Tag, ViewModel.Selection));
    }

    private void OnSidebarSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: SidebarDestination destination })
        {
            ViewModel.Selection = destination;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Selection)) ShowSelection();
    }

    private void ShowSelection()
    {
        bool isLocation = ViewModel.Selection?.IsLocation ?? true;
        LocationEmptyState.Visibility = isLocation ? Visibility.Visible : Visibility.Collapsed;
        InsightEmptyState.Visibility = isLocation ? Visibility.Collapsed : Visibility.Visible;
        if (ViewModel.Selection is { } selection) InsightEmptyIcon.Glyph = selection.Glyph;
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
