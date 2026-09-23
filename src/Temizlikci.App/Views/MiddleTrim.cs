using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Temizlikci.App.Views;

/// <summary>
/// Shortens a TextBlock's text in the middle to fit its width — for paths, whose start (the drive) and end (the item)
/// matter more than the folders between. Set <c>views:MiddleTrim.Text</c> instead of <c>Text</c>; the full text should
/// also be in a tooltip.
/// </summary>
public static class MiddleTrim
{
    private const string Ellipsis = "…";

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(MiddleTrim), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(TextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(TextProperty);
    }

    public static void SetText(TextBlock element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBlock block) return;
        block.SizeChanged -= OnSizeChanged;
        block.SizeChanged += OnSizeChanged;
        block.TextWrapping = TextWrapping.NoWrap;
        Fit(block, block.ActualWidth);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is TextBlock block && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) Fit(block, e.NewSize.Width);
    }

    /// <summary>The longest middle-shortened text that fits, found by halving: measuring is the only way to know.</summary>
    private static void Fit(TextBlock block, double width)
    {
        string text = GetText(block) ?? string.Empty;
        if (width <= 0 || Measure(block, text) <= width)
        {
            block.Text = text;
            return;
        }
        int low = 0, high = text.Length - 1;
        while (low < high)
        {
            int keep = (low + high + 1) / 2;
            if (Measure(block, Shortened(text, keep)) <= width) low = keep;
            else high = keep - 1;
        }
        block.Text = Shortened(text, low);
    }

    /// <summary>Keeps <paramref name="keep"/> characters, a little more of the end than the start.</summary>
    private static string Shortened(string text, int keep)
    {
        int head = keep * 2 / 5;
        return text[..head] + Ellipsis + text[^(keep - head)..];
    }

    private static double Measure(TextBlock block, string text)
    {
        var probe = new TextBlock
        {
            Text = text,
            FontSize = block.FontSize,
            FontFamily = block.FontFamily,
            FontWeight = block.FontWeight,
            FontStyle = block.FontStyle,
            CharacterSpacing = block.CharacterSpacing,
        };
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }
}
