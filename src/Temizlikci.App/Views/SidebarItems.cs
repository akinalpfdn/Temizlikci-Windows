using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Temizlikci.Presentation.Main;

namespace Temizlikci.App.Views;

/// <summary>Builds sidebar rows: glyph, title, and an optional right-aligned badge such as a drive's free space.</summary>
internal static class SidebarItems
{
    public static NavigationViewItem Make(SidebarDestination destination, string title, string? badge)
    {
        var item = new NavigationViewItem { Icon = new FontIcon { Glyph = destination.Glyph }, Tag = destination };
        Update(item, title, badge);
        return item;
    }

    public static void Update(NavigationViewItem item, string title, string? badge)
    {
        var grid = new Grid { ColumnSpacing = (double)Application.Current.Resources["SpacingSmall"] };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (badge is not null)
        {
            var badgeText = new TextBlock { Text = badge, Style = (Style)Application.Current.Resources["SidebarBadgeTextStyle"] };
            Grid.SetColumn(badgeText, 1);
            grid.Children.Add(badgeText);
        }
        item.Content = grid;
        AutomationProperties.SetName(item, badge is null ? title : $"{title}, {badge}");
    }
}
