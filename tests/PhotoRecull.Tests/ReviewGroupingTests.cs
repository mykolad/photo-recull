using PhotoRecull.Core;
using PhotoRecull.Core.Library;

namespace PhotoRecull.Tests;

public class ReviewGroupingTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static PhotoFile PhotoAt(string name, DateTimeOffset? time) =>
        new($@"C:\fake\{name}", name, time, IsIphone: time.HasValue, TimestampSource: time.HasValue ? "exif" : null);

    private static DateTimeOffset At(int day, int hour, int minute, int second, int millisecond = 0) =>
        new(2026, 7, day, hour, minute, second, millisecond, TimeSpan.Zero);

    [Fact]
    public void GroupsByDayNewestFirst_AndKeepsFramesWithin2SecondsTogether()
    {
        var burstFirst = PhotoAt("a.heic", At(14, 10, 0, 0));
        var burstSecond = PhotoAt("b.heic", At(14, 10, 0, 1, 500));
        var burstThird = PhotoAt("c.heic", At(14, 10, 0, 3));
        var later = PhotoAt("d.heic", At(14, 18, 0, 0));
        var nextDay = PhotoAt("e.heic", At(15, 9, 0, 0));

        var days = ReviewGrouping.NewestFirstByDayWithBursts([burstFirst, later, nextDay, burstThird, burstSecond], Utc);

        Assert.Equal([new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 14)], days.Select(d => d.Day));
        Assert.Equal(4, days[1].PhotoCount);
        Assert.Collection(days[1].Groups,
            g => Assert.Equal([later], g.Photos),
            g =>
            {
                Assert.True(g.IsBurst);
                Assert.Equal([burstThird, burstSecond, burstFirst], g.Photos);
                Assert.Equal(3, g.SpanSeconds);
            });
    }

    [Fact]
    public void FramesMoreThan2SecondsApart_AreSeparate()
    {
        var first = PhotoAt("a.heic", At(14, 10, 0, 0));
        var second = PhotoAt("b.heic", At(14, 10, 0, 2, 1));

        var groups = ReviewGrouping.NewestFirstByDayWithBursts([first, second], Utc).Single().Groups;

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.False(g.IsBurst));
    }

    [Fact]
    public void DaysFollowTheGivenTimeZone()
    {
        // 22:30 UTC on 14 Jul is already 15 Jul in UTC+3.
        var lateEvening = PhotoAt("a.heic", At(14, 22, 30, 0));
        var utcPlus3 = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

        Assert.Equal(new DateOnly(2026, 7, 15), ReviewGrouping.NewestFirstByDayWithBursts([lateEvening], utcPlus3).Single().Day);
        Assert.Equal(new DateOnly(2026, 7, 14), ReviewGrouping.NewestFirstByDayWithBursts([lateEvening], Utc).Single().Day);
    }

    [Fact]
    public void PhotosWithoutACaptureTime_ComeLastWithoutADay()
    {
        var dated = PhotoAt("a.heic", At(14, 10, 0, 0));
        var undated = PhotoAt("screenshot.png", null);

        var days = ReviewGrouping.NewestFirstByDayWithBursts([undated, dated], Utc);

        Assert.Equal([new DateOnly(2026, 7, 14), (DateOnly?)null], days.Select(d => d.Day));
        Assert.Equal([undated], days[1].Groups.Single().Photos);
    }
}
