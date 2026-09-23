using System.Globalization;
using Temizlikci.Presentation.Strings;

namespace Temizlikci.Presentation.Formatting;

/// <summary>
/// Sizes, shares, counts and dates, formatted the same way everywhere in the app. Sizes follow Explorer's
/// <c>StrFormatByteSize</c>: 1024-based units, three significant digits, truncated rather than rounded, and a switch
/// to the next unit at 1000 so a value never shows four digits (DECISIONS 2026-09-23).
/// </summary>
public static class Format
{
    private const double UnitStep = 1024;
    private const double NextUnitAt = 1000;

    public static string Bytes(long value) => BytesInCulture(value, CultureInfo.CurrentCulture);

    public static string BytesInCulture(long value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        long magnitude = value == long.MinValue ? long.MaxValue : Math.Abs(value);
        string sign = value < 0 ? culture.NumberFormat.NegativeSign : string.Empty;
        if (magnitude == 1) return sign + L10n.FormatOneByte;
        if (magnitude < NextUnitAt) return sign + L10n.FormatBytes(magnitude.ToString("N0", culture));

        double scaled = magnitude;
        int unit = 0;
        do
        {
            scaled /= UnitStep;
            unit++;
        } while (scaled >= NextUnitAt && unit < 5);

        string number = Truncated(scaled, culture);
        string text = unit switch
        {
            1 => L10n.FormatKilobytes(number),
            2 => L10n.FormatMegabytes(number),
            3 => L10n.FormatGigabytes(number),
            4 => L10n.FormatTerabytes(number),
            _ => L10n.FormatPetabytes(number),
        };
        return sign + text;
    }

    /// <summary>Three significant digits, cut off like Explorer does: 1.999 KB reads "1.99 KB", never "2.00 KB".</summary>
    private static string Truncated(double value, CultureInfo culture)
    {
        int decimals = value < 10 ? 2 : value < 100 ? 1 : 0;
        double factor = Math.Pow(10, decimals);
        double cut = Math.Floor(value * factor) / factor;
        return cut.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), culture);
    }

    /// <summary>A share such as 0.42 as "42%"; shares under 1% keep one decimal so they don't all read "0%".</summary>
    public static string Percent(double fraction) =>
        fraction.ToString(Math.Abs(fraction) < 0.01 ? "P1" : "P0", CultureInfo.CurrentCulture);

    public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Date(DateTime value) => value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    public static string DateAndTime(DateTime value)
    {
        var local = value.ToLocalTime();
        return local.ToString("d", CultureInfo.CurrentCulture) + " " + local.ToString("t", CultureInfo.CurrentCulture);
    }

    /// <summary>"just now", "5 minutes ago", "yesterday", "3 days ago", or "on 9/14/2026" for anything older.</summary>
    public static string Relative(DateTime value, DateTime now)
    {
        var elapsed = now.ToUniversalTime() - value.ToUniversalTime();
        if (elapsed < TimeSpan.FromMinutes(1)) return L10n.RelativeJustNow;
        if (elapsed < TimeSpan.FromHours(1)) return L10n.RelativeMinutesAgo((int)elapsed.TotalMinutes);
        var localNow = now.ToLocalTime().Date;
        var localValue = value.ToLocalTime().Date;
        int days = (localNow - localValue).Days;
        if (days == 0) return L10n.RelativeHoursAgo(Math.Max(1, (int)elapsed.TotalHours));
        if (days == 1) return L10n.RelativeYesterday;
        if (days < 7) return L10n.RelativeDaysAgo(days);
        return L10n.RelativeOnDate(Date(value));
    }
}
