namespace PhotoRecull.Core.Library;

public sealed record DayRange(DateOnly From, DateOnly To);

public static class RangeChecks
{
    // Days in the range before the earliest or after the latest picked photo:
    // anything on the targets from those days has nothing to match and would be
    // flagged for removal.
    public static List<DayRange> DaysOutsideThePick(
        DateOnly from, DateOnly to, DateOnly earliestPickedDay, DateOnly latestPickedDay)
    {
        var daysOutside = new List<DayRange>();
        if (from < earliestPickedDay)
            daysOutside.Add(new DayRange(from, to < earliestPickedDay ? to : earliestPickedDay.AddDays(-1)));
        if (to > latestPickedDay)
            daysOutside.Add(new DayRange(from > latestPickedDay ? from : latestPickedDay.AddDays(1), to));
        return daysOutside;
    }

    public static bool LeavesSomePickedPhotosOutside(
        DateOnly from, DateOnly to, DateOnly earliestPickedDay, DateOnly latestPickedDay) =>
        from > earliestPickedDay || to < latestPickedDay;

    public static List<Cleanup> OverlappingCleanups(
        IEnumerable<Cleanup> allCleanups, Guid? currentCleanupId, DateOnly from, DateOnly to) =>
        allCleanups
            .Where(c => c.Id != currentCleanupId && c.From <= to && c.To >= from)
            .OrderBy(c => c.From)
            .ToList();
}
