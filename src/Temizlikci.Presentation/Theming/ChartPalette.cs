namespace Temizlikci.Presentation.Theming;

/// <summary>An sRGB color with alpha, free of any UI framework so the palette can be tested.</summary>
public readonly record struct Rgb(byte R, byte G, byte B, byte A = 255)
{
    public static Rgb FromHex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>This color moved toward <paramref name="other"/> by <paramref name="amount"/> (0 = unchanged).</summary>
    public Rgb Mix(Rgb other, double amount) => new(
        Lerp(R, other.R, amount), Lerp(G, other.G, amount), Lerp(B, other.B, amount), Lerp(A, other.A, amount));

    private static byte Lerp(byte from, byte to, double amount) =>
        (byte)Math.Round(from + (to - from) * Math.Clamp(amount, 0, 1));

    /// <summary>WCAG relative luminance.</summary>
    public double RelativeLuminance =>
        0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

    private static double Linear(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>WCAG contrast ratio between two opaque colors.</summary>
    public static double Contrast(Rgb first, Rgb second)
    {
        double lighter = Math.Max(first.RelativeLuminance, second.RelativeLuminance);
        double darker = Math.Min(first.RelativeLuminance, second.RelativeLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }
}

/// <summary>Which variant of the chart colors to draw with.</summary>
public enum ChartAppearance
{
    Light,
    Dark,
    /// <summary>A High Contrast theme with a light window background.</summary>
    HighContrastLight,
    /// <summary>A High Contrast theme with a dark window background.</summary>
    HighContrastDark,
}

/// <summary>
/// Colors for chart content only; controls and text keep system colors. The values are the macOS colorsets, which
/// were validated for adjacent segments and color-vision deficiencies on #FFFFFF / #1E1E1E — the same surfaces the
/// Windows content area uses. Order matters: it is the colorblind-safety mechanism.
/// </summary>
public static class ChartPalette
{
    public const int SlotCount = 8;

    private static readonly Rgb[] SlotsLight = Hex(0x2a78d6, 0xeb6834, 0x1baf7a, 0xeda100, 0xe87ba4, 0x008300, 0x4a3aa7, 0xe34948);
    private static readonly Rgb[] SlotsDark = Hex(0x3987e5, 0xd95926, 0x199e70, 0xc98500, 0xd55181, 0x008300, 0x9085e9, 0xe66767);
    private static readonly Rgb[] SlotsHighLight = Hex(0x2a78d6, 0xd86030, 0x179769, 0xb47a00, 0xc3678a, 0x008300, 0x4a3aa7, 0xe34948);
    private static readonly Rgb[] SlotsHighDark = Hex(0x3987e5, 0xdb602f, 0x199e70, 0xc98500, 0xd85b89, 0x2e992e, 0x9085e9, 0xe66767);

    /// <summary>The content surface behind the chart; also paints the gaps between segments.</summary>
    public static Rgb Surface(ChartAppearance appearance) => appearance switch
    {
        ChartAppearance.Light or ChartAppearance.HighContrastLight => Rgb.FromHex(0xffffff),
        _ => Rgb.FromHex(0x1e1e1e),
    };

    public static Rgb Slot(int index, ChartAppearance appearance)
    {
        var slots = appearance switch
        {
            ChartAppearance.Light => SlotsLight,
            ChartAppearance.Dark => SlotsDark,
            ChartAppearance.HighContrastLight => SlotsHighLight,
            _ => SlotsHighDark,
        };
        return slots[((index % SlotCount) + SlotCount) % SlotCount];
    }

    /// <summary>Segments beyond the last slot, smaller items, and merged slivers.</summary>
    public static Rgb Neutral(ChartAppearance appearance) => appearance switch
    {
        ChartAppearance.Light => Rgb.FromHex(0xc9c8c3),
        ChartAppearance.Dark => Rgb.FromHex(0x4b4b48),
        ChartAppearance.HighContrastLight => Rgb.FromHex(0x9d9c96),
        _ => Rgb.FromHex(0x6b6a66),
    };

    /// <summary>Segments faded out while Highlight Reclaimable is on; also the base under hatching.</summary>
    public static Rgb Dimmed(ChartAppearance appearance) => appearance switch
    {
        ChartAppearance.Light => Rgb.FromHex(0xe6e5e1),
        ChartAppearance.Dark => Rgb.FromHex(0x2f2f2d),
        ChartAppearance.HighContrastLight => Rgb.FromHex(0xd4d3ce),
        _ => Rgb.FromHex(0x3d3d3a),
    };

    /// <summary>Stripes for space no folder accounts for, and for folders that couldn't be read.</summary>
    public static Rgb Hatch(ChartAppearance appearance) => appearance switch
    {
        ChartAppearance.Light => Rgb.FromHex(0xb9b8b2),
        ChartAppearance.Dark => Rgb.FromHex(0x5a5a56),
        ChartAppearance.HighContrastLight => Rgb.FromHex(0x8f8e88),
        _ => Rgb.FromHex(0x7a7a75),
    };

    /// <summary>Safe to Remove, as a segment fill. Always paired with a symbol and a label.</summary>
    public static Rgb SafeFill => Rgb.FromHex(0x0ca30c);

    /// <summary>Remove with Tool, as a segment fill. Always paired with a symbol and a label.</summary>
    public static Rgb ToolFill => Rgb.FromHex(0xfab219);

    public static Rgb LabelOnLight => new(0, 0, 0, 209);
    public static Rgb LabelOnDark => new(255, 255, 255);

    /// <summary>Deeper rings mix the slot color toward the surface (approved macOS design: ~30% / ~52% in light mode).</summary>
    public static Rgb Tint(int slot, int depth, ChartAppearance appearance)
    {
        bool dark = appearance is ChartAppearance.Dark or ChartAppearance.HighContrastDark;
        double amount = depth switch
        {
            <= 1 => 0,
            2 => dark ? 0.22 : 0.30,
            _ => dark ? 0.40 : 0.52,
        };
        var color = Slot(slot, appearance);
        return amount == 0 ? color : color.Mix(Surface(appearance), amount);
    }

    /// <summary>Dark or light label ink, whichever reads better on <paramref name="fill"/>.</summary>
    public static Rgb LabelInk(Rgb fill) => fill.RelativeLuminance > 0.18 ? LabelOnLight : LabelOnDark;

    private static Rgb[] Hex(params uint[] values) => values.Select(Rgb.FromHex).ToArray();
}
