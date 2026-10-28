namespace PhotoRecull.Core.Library;

public sealed record BurstGroup(List<PhotoFile> Photos)
{
    public bool IsBurst => Photos.Count > 1;

    // Rounded up, so a burst always reads as "within at least 1 s".
    public int SpanSeconds => IsBurst
        ? Math.Max(1, (int)Math.Ceiling((Photos[0].Timestamp!.Value - Photos[^1].Timestamp!.Value).Duration().TotalSeconds))
        : 0;
}

// Day is null for photos without a capture time (only ever in Skipped).
public sealed record DayGroup(DateOnly? Day, List<BurstGroup> Groups)
{
    public int PhotoCount => Groups.Sum(g => g.Photos.Count);
}

public static class ReviewGrouping
{
    // Frames this close together come from one press of the shutter (a burst, or
    // a quick series), so they're shown side by side to compare.
    public static readonly TimeSpan MaxGapWithinBurst = TimeSpan.FromSeconds(2);

    public static List<DayGroup> NewestFirstByDayWithBursts(IEnumerable<PhotoFile> photos, TimeZoneInfo dayTimeZone)
    {
        var days = new List<DayGroup>();
        var withTime = photos.Where(p => p.Timestamp.HasValue).OrderByDescending(p => p.Timestamp).ToList();

        foreach (var photo in withTime)
        {
            var day = DateRangeFilter.CaptureDay(photo.Timestamp!.Value, dayTimeZone);
            if (days.Count == 0 || days[^1].Day != day)
                days.Add(new DayGroup(day, []));

            var groups = days[^1].Groups;
            var previous = groups.Count > 0 ? groups[^1].Photos[^1] : null;
            if (previous is not null && previous.Timestamp!.Value - photo.Timestamp.Value <= MaxGapWithinBurst)
                groups[^1].Photos.Add(photo);
            else
                groups.Add(new BurstGroup([photo]));
        }

        var withoutTime = photos.Where(p => !p.Timestamp.HasValue).OrderBy(p => p.FileName).ToList();
        if (withoutTime.Count > 0)
            days.Add(new DayGroup(null, withoutTime.Select(p => new BurstGroup([p])).ToList()));
        return days;
    }
}
