namespace PhotoRecull.Core.Library;

public sealed record CleanupNeighbors(DateOnly? PreviousCleanupEnd, DateOnly? NextCleanupStart)
{
    public DateOnly? EarliestAllowedStart => PreviousCleanupEnd?.AddDays(1);

    public DateOnly? LatestAllowedEnd => NextCleanupStart?.AddDays(-1);

    // A new cleanup continues after every existing one, so it has no next neighbor.
    public static CleanupNeighbors For(IReadOnlyCollection<Cleanup> allCleanups, Cleanup? current)
    {
        var others = allCleanups.Where(c => c.Id != current?.Id).ToList();
        var previousEnds = current is null
            ? others.Select(c => c.To)
            : others.Where(c => c.To < current.From).Select(c => c.To);
        var nextStarts = current is null
            ? []
            : others.Where(c => c.From > current.To).Select(c => c.From);

        return new CleanupNeighbors(
            previousEnds.Select(d => (DateOnly?)d).Max(),
            nextStarts.Select(d => (DateOnly?)d).Min());
    }
}
