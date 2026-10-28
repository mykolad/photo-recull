namespace PhotoRecull.Core.Library;

public abstract record LibraryEntry(DateOnly From, DateOnly To)
{
    public int DayCount => To.DayNumber - From.DayNumber + 1;
}

public sealed record CleanupEntry(Cleanup Cleanup) : LibraryEntry(Cleanup.From, Cleanup.To);

public sealed record NotCleanedGap(DateOnly From, DateOnly To) : LibraryEntry(From, To);

public static class LibraryTimeline
{
    public static List<LibraryEntry> CleanupsAndGapsNewestFirst(IEnumerable<Cleanup> cleanups, DateOnly today)
    {
        var cleanupList = cleanups.ToList();
        return cleanupList
            .Select(c => (LibraryEntry)new CleanupEntry(c))
            .Concat(FindNotCleanedGaps(cleanupList, today))
            .OrderByDescending(e => e.To)
            .ThenByDescending(e => e.From)
            .ToList();
    }

    // Days before the first cleanup aren't a gap: the user may not have been
    // backing up then, and there's no earlier date to measure from.
    public static List<NotCleanedGap> FindNotCleanedGaps(IEnumerable<Cleanup> cleanups, DateOnly today)
    {
        var gaps = new List<NotCleanedGap>();
        DateOnly? coveredThrough = null;

        foreach (var cleanup in cleanups.OrderBy(c => c.From))
        {
            if (coveredThrough is { } covered && cleanup.From > covered.AddDays(1))
                gaps.Add(new NotCleanedGap(covered.AddDays(1), cleanup.From.AddDays(-1)));

            if (coveredThrough is null || cleanup.To > coveredThrough)
                coveredThrough = cleanup.To;
        }

        if (coveredThrough is { } lastCovered && lastCovered < today)
            gaps.Add(new NotCleanedGap(lastCovered.AddDays(1), today));

        return gaps;
    }
}
