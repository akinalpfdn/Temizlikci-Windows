using System.Globalization;
using Temizlikci.Presentation.Formatting;

namespace Temizlikci.Tests.Presentation;

public sealed class FormatTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1L, "1 byte")]
    [InlineData(999L, "999 bytes")]
    [InlineData(1000L, "0.97 KB")]
    [InlineData(1024L, "1.00 KB")]
    [InlineData(1536L, "1.50 KB")]
    [InlineData(2047L, "1.99 KB")]
    [InlineData(10240L, "10.0 KB")]
    [InlineData(123L * 1024, "123 KB")]
    [InlineData(1023L * 1024, "0.99 MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5.00 GB")]
    [InlineData(1536L * 1024 * 1024 * 1024, "1.50 TB")]
    public void Should_MatchExplorer_When_FormattingSizes(long bytes, string expected)
    {
        Assert.Equal(expected, Format.BytesInCulture(bytes, English));
    }

    [Fact]
    public void Should_KeepTheSign_When_TheSizeIsNegative()
    {
        Assert.Equal("-1.50 KB", Format.BytesInCulture(-1536, English));
    }

    [Fact]
    public void Should_UseTheCultureDecimalSeparator_When_FormattingSizes()
    {
        Assert.Equal("1,50 KB", Format.BytesInCulture(1536, CultureInfo.GetCultureInfo("tr-TR")));
    }

    [Fact]
    public void Should_KeepOneDecimal_When_AShareIsUnderOnePercent()
    {
        using var _ = new CultureScope(English);
        Assert.Equal("0.5%", Format.Percent(0.005).Replace(" ", "", StringComparison.Ordinal));
        Assert.Equal("42%", Format.Percent(0.42).Replace(" ", "", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(5 * 60, "5 minutes ago")]
    public void Should_DescribeRecentTimes_When_FormattingRelativeDates(double secondsAgo, string expected)
    {
        using var _ = new CultureScope(English);
        var now = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Local);
        Assert.Equal(expected, Format.Relative(now.AddSeconds(-secondsAgo), now));
    }

    [Fact]
    public void Should_SayYesterday_When_TheDateIsThePreviousDay()
    {
        using var _ = new CultureScope(English);
        var now = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Local);
        Assert.Equal("yesterday", Format.Relative(now.AddHours(-20), now));
    }

    [Fact]
    public void Should_CountDays_When_TheDateIsThisWeek()
    {
        using var _ = new CultureScope(English);
        var now = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Local);
        Assert.Equal("3 days ago", Format.Relative(now.AddDays(-3), now));
    }

    [Fact]
    public void Should_ShowTheDate_When_TheDateIsAWeekOrMoreAgo()
    {
        using var _ = new CultureScope(English);
        var now = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Local);
        Assert.Equal("on 9/13/2026", Format.Relative(now.AddDays(-10), now));
    }

    /// <summary>Sets the current culture for one test and restores it afterwards.</summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(CultureInfo value)
        {
            CultureInfo.CurrentCulture = value;
            CultureInfo.CurrentUICulture = value;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}
