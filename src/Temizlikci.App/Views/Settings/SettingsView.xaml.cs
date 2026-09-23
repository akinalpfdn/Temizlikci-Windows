using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Temizlikci.Domain.History;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.Settings;

/// <summary>The app's few settings: refresh age, starting as administrator, and the update check.</summary>
public sealed partial class SettingsView : UserControl
{
    private readonly MainViewModel main;
    private bool loading = true;

    public SettingsView(MainViewModel main)
    {
        this.main = main;
        InitializeComponent();
        foreach (var period in RefreshPeriods.All) RefreshBox.Items.Add(new ComboBoxItem { Content = Title(period), Tag = period });
        RefreshBox.SelectedIndex = RefreshPeriods.All.ToList().IndexOf(main.Settings.RefreshPeriod);
        AdministratorSwitch.IsOn = main.Settings.StartAsAdministrator;
        UpdatesSwitch.IsOn = main.Settings.CheckForUpdates;
        loading = false;
    }

    private static string Title(RefreshPeriod period) => period switch
    {
        RefreshPeriod.Day => L10n.SettingsRefreshDay,
        RefreshPeriod.ThreeDays => L10n.SettingsRefreshThreeDays,
        RefreshPeriod.Week => L10n.SettingsRefreshWeek,
        _ => L10n.SettingsRefreshNever,
    };

    private void OnRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || RefreshBox.SelectedItem is not ComboBoxItem { Tag: RefreshPeriod period }) return;
        main.Settings = main.Settings with { RefreshPeriod = period };
    }

    private void OnAdministratorToggled(object sender, RoutedEventArgs e)
    {
        if (!loading) main.Settings = main.Settings with { StartAsAdministrator = AdministratorSwitch.IsOn };
    }

    private void OnUpdatesToggled(object sender, RoutedEventArgs e)
    {
        if (!loading) main.Settings = main.Settings with { CheckForUpdates = UpdatesSwitch.IsOn };
    }
}
