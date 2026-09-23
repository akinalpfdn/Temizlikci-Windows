using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views;

/// <summary>Creates the view for an insight destination (Developer, What Grew, Large Files, Recycle Bin).</summary>
internal static class InsightViews
{
    public static UIElement For(SidebarDestination destination, MainViewModel main, LocationServices services) => destination.Kind switch
    {
        _ => EmptyState(destination.Glyph, L10n.InsightsNotScannedTitle, null),
    };

    /// <summary>A centered icon, title and message, with an optional button.</summary>
    public static UIElement EmptyState(string glyph, string title, string? message, (string Text, Action Action)? button = null)
    {
        var panel = new StackPanel { Style = (Style)Application.Current.Resources["EmptyStatePanelStyle"] };
        panel.Children.Add(new FontIcon { Glyph = glyph, Style = (Style)Application.Current.Resources["EmptyStateIconStyle"] });
        panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["EmptyStateTitleTextStyle"] });
        if (message is not null) panel.Children.Add(new TextBlock { Text = message, Style = (Style)Application.Current.Resources["EmptyStateMessageTextStyle"] });
        if (button is { } action)
        {
            var control = new Button { Content = action.Text, Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Center };
            control.Click += (_, _) => action.Action();
            panel.Children.Add(control);
        }
        return panel;
    }
}
