namespace PhotoRecull.Core;

public static class DateRangeFilter
{
    // Photos with no capture time can't be placed in a range, so they're kept
    // regardless of date, and the user still sees them reported as skipped.
    // Days are calendar days in captureDayTimeZone (UTC when not given, as the CLI
    // documents); the app passes the user's local time zone so "1 Aug" means
    // their 1 Aug.
    public static List<PhotoFile> KeepPhotosCapturedInRange(
        IEnumerable<PhotoFile> photos, DateOnly? firstDay, DateOnly? lastDay, TimeZoneInfo? captureDayTimeZone = null)
    {
        var zone = captureDayTimeZone ?? TimeZoneInfo.Utc;
        return photos
            .Where(p => !p.Timestamp.HasValue || IsWithin(CaptureDay(p.Timestamp.Value, zone), firstDay, lastDay))
            .ToList();
    }

    public static DateOnly CaptureDay(DateTimeOffset timestamp, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, zone).DateTime);

    private static bool IsWithin(DateOnly day, DateOnly? firstDay, DateOnly? lastDay) =>
        (firstDay is null || day >= firstDay) && (lastDay is null || day <= lastDay);
}
