using Temizlikci.Presentation.Theming;

namespace Temizlikci.Tests.Presentation;

public sealed class ChartPaletteTests
{
    public static TheoryData<ChartAppearance> Appearances => new(Enum.GetValues<ChartAppearance>());

    [Theory]
    [MemberData(nameof(Appearances))]
    public void Should_OfferEightDistinctSlots_When_DrawingInAnyAppearance(ChartAppearance appearance)
    {
        var slots = Enumerable.Range(0, ChartPalette.SlotCount).Select(index => ChartPalette.Slot(index, appearance)).ToList();
        Assert.Equal(ChartPalette.SlotCount, slots.Distinct().Count());
    }

    [Fact]
    public void Should_KeepTheValidatedMacColors_When_DrawingInLightMode()
    {
        Assert.Equal(Rgb.FromHex(0x2a78d6), ChartPalette.Slot(0, ChartAppearance.Light));
        Assert.Equal(Rgb.FromHex(0xe34948), ChartPalette.Slot(7, ChartAppearance.Light));
        Assert.Equal(Rgb.FromHex(0x9085e9), ChartPalette.Slot(6, ChartAppearance.Dark));
    }

    [Fact]
    public void Should_WrapAround_When_ASlotIndexIsPastTheLast()
    {
        Assert.Equal(ChartPalette.Slot(1, ChartAppearance.Light), ChartPalette.Slot(9, ChartAppearance.Light));
    }

    [Fact]
    public void Should_UseTheSlotColor_When_TheSegmentIsOnTheInnerRing()
    {
        Assert.Equal(ChartPalette.Slot(2, ChartAppearance.Dark), ChartPalette.Tint(2, 1, ChartAppearance.Dark));
    }

    [Fact]
    public void Should_FadeTowardTheSurface_When_TheRingIsDeeper()
    {
        var surface = ChartPalette.Surface(ChartAppearance.Light);
        var first = ChartPalette.Tint(0, 1, ChartAppearance.Light);
        var second = ChartPalette.Tint(0, 2, ChartAppearance.Light);
        var third = ChartPalette.Tint(0, 3, ChartAppearance.Light);
        Assert.True(Rgb.Contrast(second, surface) < Rgb.Contrast(first, surface));
        Assert.True(Rgb.Contrast(third, surface) < Rgb.Contrast(second, surface));
    }

    [Fact]
    public void Should_UseDarkInk_When_TheSegmentIsLight()
    {
        Assert.Equal(ChartPalette.LabelOnLight, ChartPalette.LabelInk(Rgb.FromHex(0xeda100)));
    }

    [Fact]
    public void Should_UseLightInk_When_TheSegmentIsDark()
    {
        Assert.Equal(ChartPalette.LabelOnDark, ChartPalette.LabelInk(Rgb.FromHex(0x4a3aa7)));
    }

    [Theory]
    [InlineData(ChartAppearance.Light)]
    [InlineData(ChartAppearance.Dark)]
    public void Should_SetSafeAndToolApart_When_HighlightingReclaimableSpace(ChartAppearance appearance)
    {
        Assert.NotEqual(ChartPalette.SafeFill, ChartPalette.ToolFill);
        Assert.NotEqual(ChartPalette.Dimmed(appearance), ChartPalette.SafeFill);
    }
}
