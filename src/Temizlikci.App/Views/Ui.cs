using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Temizlikci.App.Views.Overview;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.History;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views;

/// <summary>Building blocks for views built in code, all drawn from the theme's resources.</summary>
internal static class Ui
{
    /// <summary>The Windows convention for "this asks for administrator rights": the UAC shield.</summary>
    private const string ShieldGlyph = "\uEA18";

    public static TextBlock Text(string text, string style) => new() { Text = text, Style = (Style)Application.Current.Resources[style] };

    public static TextBlock Wrapped(string text, string style)
    {
        var block = Text(text, style);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    public static double Double(string key) => (double)Application.Current.Resources[key];

    public static Thickness Thickness(string key) => (Thickness)Application.Current.Resources[key];

    public static CornerRadius Radius(string key) => (CornerRadius)Application.Current.Resources[key];

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static Button Button(string text, Action action, bool needsAdministrator = false)
    {
        var button = new Button();
        if (needsAdministrator)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Double("SpacingXSmall") };
            content.Children.Add(new FontIcon { Glyph = ShieldGlyph, FontSize = Double("CaptionFontSize") });
            content.Children.Add(new TextBlock { Text = text });
            button.Content = content;
            ToolTipService.SetToolTip(button, L10n.ToolsNeedsAdministrator);
        }
        else
        {
            button.Content = text;
        }
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A rounded card on the page's surface.</summary>
    public static Border Framed(UIElement content, bool padded = true) => new()
    {
        Child = content,
        Padding = padded ? Thickness("CardPadding") : new Thickness(0),
        CornerRadius = Radius("MediumRadius"),
        Background = Brush("CardBackgroundFillColorSecondaryBrush"),
        BorderBrush = Brush("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
    };

    public static Border Divider() => new() { Height = 1, Background = Brush("ContentDividerBrush") };

    /// <summary>A safety level as glyph and label in its ink — never color alone.</summary>
    public static StackPanel SafetyBadge(SafetyLevel level)
    {
        var badge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Double("SpacingXSmall"), VerticalAlignment = VerticalAlignment.Center };
        badge.Children.Add(new FontIcon { Glyph = SafetyTexts.Glyph(level), FontSize = Double("CaptionFontSize"), Foreground = SafetyTexts.Ink(level) });
        var title = Text(SafetyTexts.Title(level), "BadgeTextStyle");
        title.Foreground = SafetyTexts.Ink(level);
        badge.Children.Add(title);
        return badge;
    }

    /// <summary>A neutral pill such as "Default" or "Running".</summary>
    public static Border Pill(string text) => new()
    {
        Child = Text(text, "BadgeTextStyle"),
        Padding = Thickness("BadgePadding"),
        CornerRadius = Radius("PillRadius"),
        Background = Brush("SizeBarTrackBrush"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A size change: arrow and signed amount in a pill, warm for growth and cool for shrinking; the arrow and
    /// the sign carry the direction, the color only reinforces it.</summary>
    public static Border GrowthPill(GrowthChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var ink = GrowthTexts.Ink(change.Kind);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Double("SpacingXSmall") };
        content.Children.Add(new FontIcon { Glyph = GrowthTexts.Glyph(change.Kind), FontSize = Double("CaptionFontSize"), Foreground = ink });
        var amount = Text(GrowthTexts.Signed(change.Delta), "BadgeTextStyle");
        amount.Foreground = ink;
        content.Children.Add(amount);
        var pill = new Border
        {
            Child = content,
            Padding = Thickness("BadgePadding"),
            CornerRadius = Radius("PillRadius"),
            Background = GrowthTexts.Background(change.Kind),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        string size = Format.Bytes(Math.Abs(change.Delta));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pill, change.Delta >= 0 ? L10n.GrowthGrewBy(size) : L10n.GrowthShrankBy(size));
        if (change.Kind == GrowthKind.Appeared) ToolTipService.SetToolTip(pill, L10n.GrowthAppearedHelp);
        return pill;
    }

    /// <summary>"Moved to the Recycle Bin" with Undo, for views that recycle through a scan model.</summary>
    public static InfoBar? RecycleConfirmation(LocationScanModel scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (scan.LastRecycled is not { } record) return null;
        var undo = new Button { Content = L10n.RecycleUndo };
        undo.Click += (_, _) => scan.PutBack(record);
        var bar = new InfoBar
        {
            IsOpen = true,
            Severity = InfoBarSeverity.Informational,
            Message = L10n.RecycleMoved(record.Name, Format.Bytes(record.Node.AllocatedSize)),
            ActionButton = undo,
        };
        bar.Closed += (_, _) => scan.DismissRecycleConfirmation();
        return bar;
    }

    /// <summary>List items whose content spans the row, so columns line up with their headers.</summary>
    public static Style StretchedItems()
    {
        var style = new Style(typeof(ListViewItem)) { BasedOn = (Style)Application.Current.Resources["DefaultListViewItemStyle"] };
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }

    /// <summary>A collapsible section whose title and summary stay visible (HIG: progressive disclosure).</summary>
    public static Expander Section(string title, string? detail, UIElement content, bool isExpanded)
    {
        var header = new Grid { ColumnSpacing = Double("SpacingMedium") };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Text(title, "SectionTitleTextStyle"));
        if (detail is not null)
        {
            var detailText = Text(detail, "SecondaryTextStyle");
            detailText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(detailText, 1);
            header.Children.Add(detailText);
        }
        var section = new Expander
        {
            Header = header,
            Content = content,
            IsExpanded = isExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        // The header is a layout, not text, so screen readers need the name spelled out.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(section, SectionName(title, detail));
        return section;
    }

    public static string SectionName(string title, string? detail) => detail is null ? title : $"{title}, {detail}";

    /// <summary>Shows a model's failed action as a dialog; offers a restart as administrator when that would fix it.</summary>
    public static async Task ShowErrorAsync(XamlRoot root, ActionError error, Func<bool>? restartAsAdministrator)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = error.Message,
            Content = error.Suggestion,
            CloseButtonText = error.OffersRestart ? L10n.AlertCancel : L10n.AlertOk,
            DefaultButton = ContentDialogButton.Close,
        };
        if (error.OffersRestart && restartAsAdministrator is not null)
        {
            dialog.PrimaryButtonText = L10n.AccessRestart;
            dialog.DefaultButton = ContentDialogButton.Primary;
        }
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && restartAsAdministrator!()) Application.Current.Exit();
    }

    /// <summary>Asks before an action that can't be undone or that stops something; true when confirmed.</summary>
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string confirm)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirm,
            CloseButtonText = L10n.AlertCancel,
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
