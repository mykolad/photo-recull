using PhotoRecull.Core.Library;

namespace PhotoRecull.Tests;

public class RangeSuggestionsTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    [Fact]
    public void DesignExample_LastCleanupEnds31Jul_PickSpans2AugTo25Sep()
    {
        var neighbors = new CleanupNeighbors(PreviousCleanupEnd: new(2026, 7, 31), NextCleanupStart: null);

        var suggestions = RangeSuggestions.For(new(2026, 8, 2), new(2026, 9, 25), neighbors, Today);

        Assert.Equal(
        [
            new RangeSuggestion("Whole months", new(2026, 8, 1), new(2026, 9, 30)),
            new RangeSuggestion("Picked photos only", new(2026, 8, 2), new(2026, 9, 25)),
            new RangeSuggestion("From last cleanup", new(2026, 8, 1), new(2026, 9, 25)),
        ], suggestions);
    }

    [Fact]
    public void NoPreviousCleanup_OnlyWholeMonthsAndPickedPhotos()
    {
        var suggestions = RangeSuggestions.For(new(2026, 8, 2), new(2026, 9, 25), new CleanupNeighbors(null, null), Today);

        Assert.Equal(["Whole months", "Picked photos only"], suggestions.Select(s => s.Label));
    }

    [Fact]
    public void WholeMonths_NeverStartsBeforeTheDayAfterThePreviousCleanup()
    {
        var neighbors = new CleanupNeighbors(PreviousCleanupEnd: new(2026, 8, 10), NextCleanupStart: null);

        var wholeMonths = RangeSuggestions.For(new(2026, 8, 12), new(2026, 9, 25), neighbors, Today)[0];

        Assert.Equal(new DateOnly(2026, 8, 11), wholeMonths.From);
    }

    [Fact]
    public void WholeMonths_NeverEndsAfterTodayOrTheDayBeforeTheNextCleanup()
    {
        var endsToday = RangeSuggestions.For(new(2026, 10, 2), new(2026, 10, 20), new CleanupNeighbors(null, null), Today)[0];
        var endsBeforeNext = RangeSuggestions.For(new(2026, 8, 2), new(2026, 8, 20),
            new CleanupNeighbors(null, NextCleanupStart: new(2026, 8, 25)), Today)[0];

        Assert.Equal(Today, endsToday.To);
        Assert.Equal(new DateOnly(2026, 8, 24), endsBeforeNext.To);
    }

    [Fact]
    public void DuplicateRanges_AreShownOnce()
    {
        // The pick covers whole months and starts the day after the previous cleanup,
        // so all three suggestions are the same range.
        var neighbors = new CleanupNeighbors(PreviousCleanupEnd: new(2026, 7, 31), NextCleanupStart: null);

        var suggestions = RangeSuggestions.For(new(2026, 8, 1), new(2026, 8, 31), neighbors, Today);

        Assert.Equal(new RangeSuggestion("Whole months", new(2026, 8, 1), new(2026, 8, 31)), Assert.Single(suggestions));
    }
}
