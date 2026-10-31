using PhotoRecull.Core;
using PhotoRecull.Core.Library;

namespace PhotoRecull.Tests;

public class ReviewTimelineTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static PhotoFile PhotoAt(string name, DateTimeOffset time) =>
        new($@"C:\fake\{name}", name, time, IsIphone: true, TimestampSource: "filename");

    private static DateTimeOffset At(int day, int hour, int minute, int second) =>
        new(2026, 7, day, hour, minute, second, TimeSpan.Zero);

    // A burst of three where only the middle frame was kept in Google Photos,
    // a single photo that stays and one kept by hand, on one day.
    private static readonly PhotoFile BurstOldest = PhotoAt("a.heic", At(14, 10, 0, 0));
    private static readonly PhotoFile BurstMiddle = PhotoAt("b.heic", At(14, 10, 0, 1));
    private static readonly PhotoFile BurstNewest = PhotoAt("c.heic", At(14, 10, 0, 2));
    private static readonly PhotoFile Single = PhotoAt("d.heic", At(14, 12, 0, 0));
    private static readonly PhotoFile KeptByHand = PhotoAt("e.heic", At(14, 18, 0, 0));

    private static readonly PhotoFile[] AllPhotos = [BurstOldest, BurstMiddle, BurstNewest, Single, KeptByHand];

    private static ReviewMark MarkOf(PhotoFile photo) =>
        photo == BurstMiddle || photo == Single ? ReviewMark.Stays
        : photo == KeptByHand ? ReviewMark.KeptByYou
        : ReviewMark.ToRemove;

    [Fact]
    public void All_ShowsEveryPhotoInOneTimeline_WithBurstsAsStacks()
    {
        var day = ReviewTimeline.NewestFirst(AllPhotos, MarkOf, ReviewFilter.All, Utc).Single();

        Assert.Collection(day.Stacks,
            s => Assert.Equal([KeptByHand], s.Shown),
            s => Assert.Equal([Single], s.Shown),
            s =>
            {
                Assert.True(s.IsBurst);
                Assert.Equal([BurstNewest, BurstMiddle, BurstOldest], s.Shown);
                Assert.Equal(2, s.SpanSeconds);
            });
    }

    [Fact]
    public void HideToRemove_KeepsTheBurstButShowsOnlyItsStayingFrame()
    {
        var day = ReviewTimeline.NewestFirst(AllPhotos, MarkOf, ReviewFilter.HideToRemove, Utc).Single();

        var burst = day.Stacks[^1];
        Assert.Equal([BurstNewest, BurstMiddle, BurstOldest], burst.Frames);
        Assert.Equal([BurstMiddle], burst.Shown);
        Assert.Equal([KeptByHand, Single], day.Stacks.Take(2).SelectMany(s => s.Shown));
        Assert.Equal(AllPhotos.Length, day.Photos.Count);
    }

    [Fact]
    public void OnlyToRemove_ShowsCandidates_IncludingThoseKeptByHand()
    {
        var day = ReviewTimeline.NewestFirst(AllPhotos, MarkOf, ReviewFilter.OnlyToRemove, Utc).Single();

        Assert.Collection(day.Stacks,
            s => Assert.Equal([KeptByHand], s.Shown),
            s => Assert.Equal([BurstNewest, BurstOldest], s.Shown));
    }

    [Fact]
    public void DaysTheFilterEmpties_AreLeftOut()
    {
        var removedDay = PhotoAt("f.heic", At(15, 9, 0, 0));

        var days = ReviewTimeline.NewestFirst([Single, removedDay], p => p == Single ? ReviewMark.Stays : ReviewMark.ToRemove,
            ReviewFilter.HideToRemove, Utc);

        Assert.Equal([new DateOnly(2026, 7, 14)], days.Select(d => d.Day));
    }

    [Fact]
    public void Cover_IsTheNewestFrameThatStays()
    {
        Assert.Equal(BurstMiddle, ReviewTimeline.Cover([BurstNewest, BurstMiddle, BurstOldest], MarkOf));
        Assert.Equal(KeptByHand, ReviewTimeline.Cover([BurstNewest, KeptByHand], MarkOf));
    }

    [Fact]
    public void Cover_OfABurstThatGoesEntirely_IsItsNewestFrame() =>
        Assert.Equal(BurstNewest, ReviewTimeline.Cover([BurstNewest, BurstOldest], MarkOf));

    [Theory]
    [InlineData(1, 4, 5, "1 stays · 4 go")]
    [InlineData(2, 1, 3, "2 stay · 1 goes")]
    [InlineData(3, 0, 3, "All 3 stay")]
    [InlineData(0, 5, 5, "All 5 go")]
    [InlineData(1, 4, 4, "1 stays · 4 go · 4 of 5 shown")]
    public void StackCaption_SaysWhatHappensToTheBurst(int staying, int toRemove, int shown, string expected) =>
        Assert.Equal(expected, ReviewTimeline.StackCaption(staying, toRemove, shown));

    [Theory]
    [InlineData(12, 5, "12 stay · 5 to remove")]
    [InlineData(1, 0, "1 stays")]
    [InlineData(0, 3, "3 to remove")]
    public void DayCaption_CountsWhatStaysAndGoes(int staying, int toRemove, string expected) =>
        Assert.Equal(expected, ReviewTimeline.DayCaption(staying, toRemove));
}
