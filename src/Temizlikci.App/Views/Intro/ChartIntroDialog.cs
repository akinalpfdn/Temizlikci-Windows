using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Temizlikci.App.Views.Overview;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.Intro;

/// <summary>
/// A brief, optional, interactive introduction to the sunburst: shown once after the first scan, skippable, and always
/// available from the Help menu. The chart is a real, clickable sunburst on example data that never touches the disk.
/// </summary>
internal static class ChartIntroDialog
{
    private const string RingsGlyph = "\uE8B7";
    private const string SizeGlyph = "\uEB05";
    private const string InteractionGlyph = "\uE7C9";
    private const string HighlightGlyph = "\uE7E6";

    public static async Task ShowAsync(XamlRoot root, LocationScanModel sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        sample.StartScan();
        double side = Ui.Double("IntroChartSide");
        var chart = new SunburstChart { Model = sample, Width = side, Height = side };

        var chartColumn = new StackPanel { Spacing = Ui.Double("SpacingSmall") };
        chartColumn.Children.Add(chart);
        var tryIt = Ui.Text(L10n.IntroTryIt, "ChartCaptionTextStyle");
        tryIt.Width = side;
        chartColumn.Children.Add(tryIt);

        var tips = new StackPanel { Spacing = Ui.Double("SpacingMedium"), Width = side };
        tips.Children.Add(Tip(RingsGlyph, L10n.IntroRings));
        tips.Children.Add(Tip(SizeGlyph, L10n.IntroSize));
        tips.Children.Add(Tip(InteractionGlyph, L10n.IntroInteraction));
        tips.Children.Add(Tip(HighlightGlyph, L10n.IntroHighlight));

        var body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Double("SpacingXLarge") };
        body.Children.Add(chartColumn);
        body.Children.Add(tips);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = L10n.IntroTitle,
            Content = body,
            PrimaryButtonText = L10n.IntroDone,
            DefaultButton = ContentDialogButton.Primary,
        };
        // The default dialog is too narrow for the chart beside the tips.
        dialog.Resources["ContentDialogMaxWidth"] = Ui.Double("IntroDialogMaxWidth");
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            sample.Dispose();
        }
    }

    private static Grid Tip(string glyph, string text)
    {
        var tip = new Grid { ColumnSpacing = Ui.Double("SpacingSmall") };
        tip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tip.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = Ui.Double("IconSize"),
            Foreground = Ui.Brush("AccentTextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, Ui.Double("SpacingXXSmall"), 0, 0),
        });
        var words = Ui.Wrapped(text, "BodyTextStyle");
        Grid.SetColumn(words, 1);
        tip.Children.Add(words);
        return tip;
    }
}
