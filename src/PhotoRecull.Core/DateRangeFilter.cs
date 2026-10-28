namespace PhotoRecull.Core;

public static class DateRangeFilter
{
    // Photos that can never be matched (non-iPhone, or no capture time) are kept
    // regardless of date, so the user still sees them reported as skipped.
    public static List<PhotoFile> KeepPhotosCapturedInRange(
        IEnumerable<PhotoFile> photos, DateOnly? firstUtcDay, DateOnly? lastUtcDay)
    {
        var rangeStartUtc = firstUtcDay.HasValue
            ? new DateTimeOffset(firstUtcDay.Value, TimeOnly.MinValue, TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        var rangeEndUtc = lastUtcDay.HasValue
            ? new DateTimeOffset(lastUtcDay.Value, TimeOnly.MaxValue, TimeSpan.Zero)
            : DateTimeOffset.MaxValue;

        return photos
            .Where(p => !p.IsIphone
                     || !p.Timestamp.HasValue
                     || (p.Timestamp.Value >= rangeStartUtc && p.Timestamp.Value <= rangeEndUtc))
            .ToList();
    }
}
