namespace PhotoRecull.Core;

public sealed record TargetScanResult(ISyncTarget Target, int PhotoFilesFound, MatchResult Match)
{
    public int IphonePhotosInRange => Match.Matched.Count + Match.Unmatched.Count;
}

public static class CleanupScanner
{
    // Each target is matched against the whole pick independently: a photo kept
    // in Google Photos keeps one copy on every target.
    public static TargetScanResult ScanAndMatch(
        ISyncTarget target,
        IReadOnlyCollection<DateTimeOffset> pickedTimestamps,
        DateOnly firstDay,
        DateOnly lastDay,
        TimeZoneInfo captureDayTimeZone,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var allPhotos = target.Scan(progress, cancellationToken);
        var photosInRange = DateRangeFilter.KeepPhotosCapturedInRange(allPhotos, firstDay, lastDay, captureDayTimeZone);
        return new TargetScanResult(target, allPhotos.Count, PhotoMatcher.Match(pickedTimestamps, photosInRange));
    }
}
