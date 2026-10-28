namespace PhotoRecull.Core.Library;

public sealed record RangeSuggestion(string Label, DateOnly From, DateOnly To);

public static class RangeSuggestions
{
    // Every suggestion is kept between the neighboring cleanups and never ends
    // after today; duplicates and empty ranges are dropped.
    public static List<RangeSuggestion> For(
        DateOnly earliestPickedDay, DateOnly latestPickedDay, CleanupNeighbors neighbors, DateOnly today)
    {
        DateOnly ClampStart(DateOnly day) =>
            neighbors.EarliestAllowedStart is { } earliest && day < earliest ? earliest : day;

        DateOnly ClampEnd(DateOnly day)
        {
            var end = day > today ? today : day;
            return neighbors.LatestAllowedEnd is { } latest && end > latest ? latest : end;
        }

        var candidates = new List<RangeSuggestion>
        {
            new("Whole months",
                ClampStart(new DateOnly(earliestPickedDay.Year, earliestPickedDay.Month, 1)),
                ClampEnd(LastDayOfMonth(latestPickedDay))),
            new("Picked photos only", ClampStart(earliestPickedDay), ClampEnd(latestPickedDay)),
        };
        if (neighbors.EarliestAllowedStart is { } dayAfterPreviousCleanup)
            candidates.Add(new("From last cleanup", dayAfterPreviousCleanup, ClampEnd(latestPickedDay)));

        return candidates
            .Where(s => s.From <= s.To)
            .DistinctBy(s => (s.From, s.To))
            .ToList();
    }

    private static DateOnly LastDayOfMonth(DateOnly day) =>
        new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));
}
