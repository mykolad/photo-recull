using System.Globalization;
using PhotoRecull.Core.Library;

namespace PhotoRecull.Tests;

public class DateRangeLabelTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    [Theory]
    [InlineData("2026-07-06", "2026-07-31", "6 Jul – 31 Jul 2026")]
    [InlineData("2025-12-01", "2026-01-06", "1 Dec 2025 – 6 Jan 2026")]
    [InlineData("2026-07-06", "2026-07-06", "6 Jul 2026")]
    public void Format_MatchesTheDesign(string from, string to, string expected)
    {
        Assert.Equal(expected, DateRangeLabel.Format(DateOnly.Parse(from), DateOnly.Parse(to), English));
    }

    [Fact]
    public void FormatUntilToday_EndsWithToday()
    {
        Assert.Equal("1 Aug 2026 – today", DateRangeLabel.FormatUntilToday(new(2026, 8, 1), English));
    }
}
