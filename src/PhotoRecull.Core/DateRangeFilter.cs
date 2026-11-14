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

    // A photo whose time zone is a guess could have been taken up to a day either
    // side of where the guess puts it. Near the range's edges it could belong to
    // the next or previous range, whose photos weren't picked, so it isn't a
    // candidate: it's set aside as skipped, and stays.
    public static MatchResult SetAsideGuessedPhotosNearTheEdges(
        MatchResult match, DateOnly? firstDay, DateOnly? lastDay, TimeZoneInfo? captureDayTimeZone = null)
    {
        var zone = captureDayTimeZone ?? TimeZoneInfo.Utc;
        var nearTheEdges = match.Unmatched.Where(p => p.TimestampOffsetGuessed && CouldBeOutside(p, firstDay, lastDay, zone)).ToList();
        return nearTheEdges.Count == 0 ? match : match with
        {
            Unmatched = match.Unmatched.Except(nearTheEdges).ToList(),
            Skipped = [.. match.Skipped, .. nearTheEdges],
        };
    }

    // Clock time is the time where the photo was taken: UTC-12 to UTC+14 puts the
    // moment itself anywhere from 14 hours before to 12 hours after it.
    private static bool CouldBeOutside(PhotoFile photo, DateOnly? firstDay, DateOnly? lastDay, TimeZoneInfo zone)
    {
        var clockTime = new DateTimeOffset(DateTime.SpecifyKind(photo.Timestamp!.Value.DateTime, DateTimeKind.Unspecified), TimeSpan.Zero);
        return !IsWithin(CaptureDay(clockTime.AddHours(-14), zone), firstDay, lastDay)
            || !IsWithin(CaptureDay(clockTime.AddHours(12), zone), firstDay, lastDay);
    }

    public static DateOnly CaptureDay(DateTimeOffset timestamp, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, zone).DateTime);

    private static bool IsWithin(DateOnly day, DateOnly? firstDay, DateOnly? lastDay) =>
        (firstDay is null || day >= firstDay) && (lastDay is null || day <= lastDay);
}
