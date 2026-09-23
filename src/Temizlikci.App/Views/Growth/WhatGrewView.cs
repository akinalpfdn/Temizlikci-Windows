using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Temizlikci.App.Views.Overview;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Tree;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Main;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.Growth;

/// <summary>The biggest changes between the two latest scans of a location, grown first, then shrunk.</summary>
internal sealed partial class WhatGrewView : UserControl
{
    private const string EmptyGlyph = "\uE9D2";
    private const string NoChangesGlyph = "\uE73E";

    private readonly MainViewModel main;
    private readonly List<LocationScanModel> watched;
    private GrowthReport? shownReport;
    private bool hasShown;

    public WhatGrewView(MainViewModel main)
    {
        this.main = main;
        watched = main.AllScans.ToList();
        foreach (var scan in watched) scan.PropertyChanged += OnScanChanged;
        Unloaded += (_, _) =>
        {
            foreach (var scan in watched) scan.PropertyChanged -= OnScanChanged;
        };
        // Without a scan this session, the comparison comes from the two latest saved summaries, read in the background.
        main.LoadSavedGrowth();
        Rebuild();
    }

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(main.GrowthScan?.Growth, shownReport)) Rebuild();
    }

    private void Rebuild()
    {
        var scan = main.GrowthScan;
        var report = scan?.Growth;
        if (hasShown && ReferenceEquals(report, shownReport)) return;
        hasShown = true;
        shownReport = report;
        if (scan is null || report is null)
        {
            Content = InsightViews.EmptyState(EmptyGlyph, L10n.GrowthEmptyTitle, L10n.GrowthEmptyMessage);
            return;
        }
        var changes = report.BiggestChanges();
        if (changes.Count == 0)
        {
            Content = InsightViews.EmptyState(NoChangesGlyph, L10n.GrowthNoChangesTitle, L10n.GrowthNoChangesMessage(Format.DateAndTime(report.PreviousDateUtc)));
            return;
        }
        Content = new ScrollViewer { Content = Page(scan, report, changes), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private StackPanel Page(LocationScanModel scan, GrowthReport report, IReadOnlyList<GrowthChange> changes)
    {
        var page = new StackPanel { Padding = Ui.Thickness("PagePadding"), Spacing = Ui.Double("SpacingSmall") };
        page.Children.Add(Ui.Text(L10n.GrowthHeader(scan.Location.DisplayName, Format.DateAndTime(report.PreviousDateUtc)), "PageTitleTextStyle"));
        page.Children.Add(Ui.Text(L10n.GrowthNet(GrowthTexts.Signed(changes.Sum(change => change.Delta))), "SecondaryTextStyle"));
        if (scan.GrowthIsFromSavedScans) page.Children.Add(Ui.Wrapped(L10n.GrowthFromSavedScans(Format.DateAndTime(report.CurrentDateUtc)), "CaptionTextStyle"));
        long largest = changes.Max(change => Math.Abs(change.Delta));
        AddSection(page, scan, L10n.GrowthGrewSection, GrowthKind.Grew, changes.Where(change => change.Delta >= 0).ToList(), largest);
        AddSection(page, scan, L10n.GrowthShrankSection, GrowthKind.Shrank, changes.Where(change => change.Delta < 0).ToList(), largest);
        return page;
    }

    private void AddSection(StackPanel page, LocationScanModel scan, string title, GrowthKind direction, List<GrowthChange> items, long largest)
    {
        if (items.Count == 0) return;
        var ink = GrowthTexts.Ink(direction);
        var header = new Grid { ColumnSpacing = Ui.Double("SpacingXSmall"), Margin = new Thickness(0, Ui.Double("SpacingMedium"), 0, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new FontIcon { Glyph = GrowthTexts.Glyph(direction), FontSize = Ui.Double("CaptionFontSize"), Foreground = ink });
        var titleText = Ui.Text(title, "BadgeTextStyle");
        titleText.Foreground = ink;
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);
        var total = Ui.Text(GrowthTexts.Signed(items.Sum(change => change.Delta)), "BadgeTextStyle");
        total.Foreground = ink;
        Grid.SetColumn(total, 2);
        header.Children.Add(total);
        page.Children.Add(header);

        var rows = new StackPanel();
        foreach (var change in items)
        {
            if (rows.Children.Count > 0) rows.Children.Add(Ui.Divider());
            rows.Children.Add(Row(scan, change, largest));
        }
        page.Children.Add(Ui.Framed(rows, padded: false));
    }

    private Grid Row(LocationScanModel scan, GrowthChange change, long largest)
    {
        var row = new Grid { ColumnSpacing = Ui.Double("SpacingMedium"), Padding = Ui.Thickness("RowPadding") };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = Ui.Double("GrowthColumnMinWidth") });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { Spacing = Ui.Double("SpacingXXSmall") };
        string name = NodePath.Name(change.Path);
        texts.Children.Add(Ui.Text(name, "BodyTextStyle"));
        var path = Ui.Text(string.Empty, "CaptionTextStyle");
        MiddleTrim.SetText(path, change.Path);
        ToolTipService.SetToolTip(path, change.Path);
        texts.Children.Add(path);
        texts.Children.Add(Bar(change, largest));
        row.Children.Add(texts);

        string? detail = change.Kind == GrowthKind.Removed ? L10n.GrowthRemoved
            : change.Previous is { } previous ? L10n.GrowthPrevious(Format.Bytes(previous))
            : null;
        if (detail is not null)
        {
            var detailText = Ui.Text(detail, "CaptionTextStyle");
            detailText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(detailText, 1);
            row.Children.Add(detailText);
        }

        var pill = Ui.GrowthPill(change);
        Grid.SetColumn(pill, 2);
        row.Children.Add(pill);

        var show = Ui.Button(L10n.GrowthShowInChart, () => main.Show(change.Path, scan));
        show.IsEnabled = change.Kind != GrowthKind.Removed && scan.HasResult;
        show.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(show, 3);
        row.Children.Add(show);
        return row;
    }

    /// <summary>How much of the biggest change this one is. Length repeats what the amount says, for people who read
    /// shape before text; it is hidden from screen readers.</summary>
    private static Grid Bar(GrowthChange change, long largest)
    {
        double fraction = largest > 0 ? Math.Min(1, (double)Math.Abs(change.Delta) / largest) : 0;
        var bar = new Grid { Height = Ui.Double("GrowthBarHeight"), Margin = new Thickness(0, Ui.Double("SpacingXXSmall"), 0, 0), CornerRadius = Ui.Radius("SmallRadius") };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star), MinWidth = Ui.Double("GrowthBarMinimumLength") });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        var track = new Border { Background = Ui.Brush("SizeBarTrackBrush") };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = GrowthTexts.Ink(change.Kind) });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(bar, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return bar;
    }
}
