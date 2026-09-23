using Microsoft.UI.Xaml;
using Temizlikci.Domain.Cleanup;
using Temizlikci.Domain.Layout;
using Temizlikci.Presentation.Theming;
using Windows.UI;

namespace Temizlikci.App.Theme;

/// <summary>Resolves chart fill roles to real colors for the theme on screen (Win2D and list swatches).</summary>
internal static class ChartColors
{
    public static ChartAppearance Appearance(ElementTheme theme)
    {
        bool dark = theme == ElementTheme.Dark;
        if (!ThemeColors.IsHighContrast) return dark ? ChartAppearance.Dark : ChartAppearance.Light;
        return dark ? ChartAppearance.HighContrastDark : ChartAppearance.HighContrastLight;
    }

    public static Color ToColor(Rgb rgb) => Color.FromArgb(rgb.A, rgb.R, rgb.G, rgb.B);

    /// <summary>The solid color of a fill role, or <c>null</c> for roles drawn as hatching or an outline.</summary>
    public static Color? For(SegmentFill fill, ChartAppearance appearance) => fill.Role switch
    {
        FillRole.Slot => ToColor(ChartPalette.Tint(fill.Slot, fill.Depth, appearance)),
        FillRole.Neutral => ToColor(ChartPalette.Neutral(appearance)),
        FillRole.Safety when fill.Safety == SafetyLevel.Safe => ToColor(ChartPalette.SafeFill),
        FillRole.Safety when fill.Safety == SafetyLevel.Tool => ToColor(ChartPalette.ToolFill),
        FillRole.Safety or FillRole.Dimmed => ToColor(ChartPalette.Dimmed(appearance)),
        _ => null,
    };

    public static Color Surface(ChartAppearance appearance) => ToColor(ChartPalette.Surface(appearance));

    public static Color Dimmed(ChartAppearance appearance) => ToColor(ChartPalette.Dimmed(appearance));

    public static Color Hatch(ChartAppearance appearance) => ToColor(ChartPalette.Hatch(appearance));

    public static Color LabelInk(Color fill) => ToColor(ChartPalette.LabelInk(new Rgb(fill.R, fill.G, fill.B, fill.A)));

    public static Color WithOpacity(Color color, double opacity) => Color.FromArgb((byte)(color.A * opacity), color.R, color.G, color.B);
}
