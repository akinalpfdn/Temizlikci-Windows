using Microsoft.UI.Xaml;
using Windows.UI;

namespace Temizlikci.App.Theme;

/// <summary>Looks up a <c>Color</c> token from Colors.xaml for a specific theme, for places XAML can't reach
/// (the system-drawn caption buttons, Win2D).</summary>
internal static class ThemeColors
{
    public static Color Get(string key, ElementTheme theme)
    {
        string dictionaryKey = IsHighContrast ? "HighContrast" : theme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(dictionaryKey, out var themed)
                && themed is ResourceDictionary resources
                && resources.TryGetValue(key, out var value)
                && value is Color color)
            {
                return color;
            }
        }
        throw new KeyNotFoundException($"Color token {key} is missing from the {dictionaryKey} theme.");
    }

    public static bool IsHighContrast => new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
}
