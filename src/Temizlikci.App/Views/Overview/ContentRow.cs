using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Temizlikci.App.Theme;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.History;
using Temizlikci.Domain.Layout;
using Temizlikci.Domain.Tree;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.App.Views.Overview;

/// <summary>How wide the list's columns are right now; every row and the header share them.</summary>
/// <param name="Compact">The list is too narrow for everything: sizes as text only, no Share column.</param>
public sealed record RowColumns(GridLength Size, GridLength Change, GridLength Share, bool Compact);

/// <summary>One list row, with everything its template shows already worked out, so the template only binds.</summary>
public sealed class ContentRow
{
    private const double BarWidth = 56;

    public ContentRow(LocationScanModel model, NodeRef node, long largest, ElementTheme theme, RowColumns columns)
    {
        Columns = columns;
        Node = node;
        Id = node.Id;
        Title = model.Title(node);
        Tooltip = LocationScanModel.ActionablePath(node) ?? Title;
        var appearance = ChartColors.Appearance(theme);
        var fill = model.DisplayFill(node);
        var solid = ChartColors.For(fill, appearance);
        SwatchBrush = new SolidColorBrush(solid ?? ChartColors.Dimmed(appearance));
        SwatchBorderBrush = new SolidColorBrush(fill.IsHatched || fill.Role == FillRole.Pending ? ChartColors.Hatch(appearance) : solid ?? ChartColors.Dimmed(appearance));
        BarBrush = new SolidColorBrush(solid ?? ChartColors.Hatch(appearance));
        SizeText = Format.Bytes(node.Node.AllocatedSize);
        BarLength = largest > 0 ? Math.Max(2, BarWidth * node.Node.AllocatedSize / largest) : 0;
        ShareText = LocationScanModel.Share(node.Node, model.CurrentFolder?.Node) is { } share ? Format.Percent(share) : string.Empty;
        IsMeasuring = model.MeasuringIds.Contains(node.Id);
        NeedsAccess = node.Node.Kind == NodeKind.Inaccessible;
        if (model.CleanupMatchFor(node) is { } match && match.Id == node.Id) Safety = match.Rule.Safety;
        if (model.GrowthFor(node) is { } change) Change = change;
    }

    public NodeRef Node { get; }
    public string Id { get; }
    public string Title { get; }
    public string Tooltip { get; }
    public Brush SwatchBrush { get; }
    public Brush SwatchBorderBrush { get; }
    public Brush BarBrush { get; }
    public string SizeText { get; }
    public double BarLength { get; }
    public string ShareText { get; }
    public bool IsMeasuring { get; }
    public bool NeedsAccess { get; }
    public SafetyLevel? Safety { get; }
    public GrowthChange? Change { get; }
    public RowColumns Columns { get; }

    public Visibility BarVisibility => Columns.Compact ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ShareVisibility => Columns.Compact ? Visibility.Collapsed : Visibility.Visible;

    public Visibility MeasuringVisibility => IsMeasuring ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NeedsAccessVisibility => NeedsAccess ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SafetyVisibility => Safety is null ? Visibility.Collapsed : Visibility.Visible;
    public string SafetyText => Safety is { } level ? SafetyTexts.Title(level) : string.Empty;
    public string SafetyGlyph => Safety is { } level ? SafetyTexts.Glyph(level) : string.Empty;
    public Brush SafetyBrush => SafetyTexts.Ink(Safety ?? SafetyLevel.Keep);
    public Visibility ChangeVisibility => Change is null ? Visibility.Collapsed : Visibility.Visible;
    public string ChangeText => Change is { } change ? GrowthTexts.Signed(change.Delta) : string.Empty;
    public string ChangeGlyph => Change is { } change ? GrowthTexts.Glyph(change.Kind) : string.Empty;
    public Brush ChangeBrush => GrowthTexts.Ink(Change?.Kind ?? GrowthKind.Grew);
    public Brush ChangeBackground => GrowthTexts.Background(Change?.Kind ?? GrowthKind.Grew);
}

/// <summary>How a safety level reads: a label, a Segoe Fluent glyph and an ink — never color alone.</summary>
internal static class SafetyTexts
{
    public static string Title(SafetyLevel level) => level switch
    {
        SafetyLevel.Safe => L10n.CleanupSafe,
        SafetyLevel.Tool => L10n.CleanupTool,
        _ => L10n.CleanupKeep,
    };

    public static string Glyph(SafetyLevel level) => level switch
    {
        SafetyLevel.Safe => "\uE73E",
        SafetyLevel.Tool => "\uE90F",
        _ => "\uE72E",
    };

    public static Brush Ink(SafetyLevel level) => (Brush)Application.Current.Resources[level switch
    {
        SafetyLevel.Safe => "StatusSafeInkBrush",
        SafetyLevel.Tool => "StatusToolInkBrush",
        _ => "StatusKeepInkBrush",
    }];
}

/// <summary>A size change: arrow glyph plus signed amount, warm for growth and cool for shrinking.</summary>
internal static class GrowthTexts
{
    public static string Signed(long bytes) => (bytes >= 0 ? "+" : "−") + Format.Bytes(Math.Abs(bytes));

    public static string Glyph(GrowthKind kind) => kind switch
    {
        GrowthKind.Grew => "\uE74A",
        GrowthKind.Shrank => "\uE74B",
        GrowthKind.Appeared => "\uE710",
        _ => "\uE738",
    };

    public static Brush Ink(GrowthKind kind) => (Brush)Application.Current.Resources[kind is GrowthKind.Grew or GrowthKind.Appeared ? "GrowthUpInkBrush" : "GrowthDownInkBrush"];

    public static Brush Background(GrowthKind kind) => (Brush)Application.Current.Resources[kind is GrowthKind.Grew or GrowthKind.Appeared ? "GrowthUpBackgroundBrush" : "GrowthDownBackgroundBrush"];
}
