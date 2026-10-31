namespace PhotoRecull.Core;

public sealed record TargetScanResult(ISyncTarget Target, int PhotoFilesFound, MatchResult Match)
{
    public int IphonePhotosInRange => Match.Matched.Count + Match.Unmatched.Count;
}

public static class CleanupScanner
{
    // Each target is matched against the pick independently: a photo kept in
    // Google Photos keeps one copy on every target. Picked photos outside the
    // range are left out, as the Range screen promises; otherwise a pick from
    // just before midnight could keep a target photo from just after it.
    public static TargetScanResult ScanAndMatch(
        ISyncTarget target,
        IReadOnlyCollection<DateTimeOffset> pickedTimestamps,
        DateOnly firstDay,
        DateOnly lastDay,
        TimeZoneInfo captureDayTimeZone,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var allPhotos = target.Scan(firstDay, lastDay, progress, cancellationToken);
        var photosInRange = DateRangeFilter.KeepPhotosCapturedInRange(allPhotos, firstDay, lastDay, captureDayTimeZone);
        var pickedInRange = pickedTimestamps.Where(t =>
        {
            var day = DateRangeFilter.CaptureDay(t, captureDayTimeZone);
            return day >= firstDay && day <= lastDay;
        });
        return new TargetScanResult(target, allPhotos.Count, PhotoMatcher.Match(pickedInRange, photosInRange));
    }
}
