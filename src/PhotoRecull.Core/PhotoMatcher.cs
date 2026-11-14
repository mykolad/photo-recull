namespace PhotoRecull.Core;

public record MatchResult(
    List<PhotoFile> Matched,
    List<PhotoFile> Unmatched,
    List<PhotoFile> Skipped    // no capture time, or not an iPhone's when only those count (see WhySkipped)
);

/// <summary>
/// Matches a target's photos against the timestamps of photos currently in
/// Google Photos.
///
/// Matching is ONE-TO-ONE: each Google photo can keep at most one target
/// file. This is essential for bursts — if you shot several frames within a
/// couple of seconds and later kept only one in Google Photos, only the
/// target frame nearest that Google timestamp is kept; the others become
/// removal candidates. A naive "any file within tolerance matches" would keep
/// the whole burst.
///
/// Google timestamps are supplied as a sequence (not a set) so duplicates are
/// preserved: if two kept frames share the same whole-second time, both
/// target files are kept.
/// </summary>
public static class PhotoMatcher
{
    // Time zones are whole hours or, in a few places, 15-minute steps, from
    // UTC-12 to UTC+14.
    private static readonly TimeSpan OffsetStep = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan EarliestOffset = TimeSpan.FromHours(-12);
    private static readonly TimeSpan LatestOffset = TimeSpan.FromHours(14);

    // onlyIphonePhotos leaves photos from other cameras skipped, as a target does
    // by default. A photo whose time zone had to be guessed (TimestampOffsetGuessed)
    // doesn't take part in the one-to-one matching: it stays whenever any Google
    // photo in timestampsAtAnyOffset (the whole pick, by default the same
    // timestamps) could be it, in its guessed time zone or any other. Without a
    // time zone, which photo of a moment was kept can't be told, so a wrong guess,
    // or another camera's photo of the same moment, can only ever keep a photo.
    // A burst from such a camera stays whole when one of its frames was kept.
    public static MatchResult Match(
        IEnumerable<DateTimeOffset> googleTimestamps,
        List<PhotoFile> photos,
        int toleranceSeconds = 2,
        bool onlyIphonePhotos = true,
        IEnumerable<DateTimeOffset>? timestampsAtAnyOffset = null)
    {
        var matched   = new List<PhotoFile>();
        var unmatched = new List<PhotoFile>();
        var skipped   = new List<PhotoFile>();

        var known = new List<PhotoFile>();
        var guessed = new List<PhotoFile>();
        foreach (var p in photos)
        {
            if (!p.Timestamp.HasValue || (onlyIphonePhotos && !p.IsIphone))
                skipped.Add(p);
            else
                (p.TimestampOffsetGuessed ? guessed : known).Add(p);
        }

        // Sort candidates by time for binary search + stable tie-breaking.
        var google = googleTimestamps.ToList();
        known.Sort((a, b) => a.Timestamp!.Value.CompareTo(b.Timestamp!.Value));
        var candTimes = known.Select(c => c.Timestamp!.Value).ToArray();
        var claimed = new bool[known.Count];

        double tol = toleranceSeconds;

        // Each Google photo claims its nearest still-available target file
        // within tolerance. Processing Google timestamps in order keeps the
        // assignment stable.
        foreach (var g in google.OrderBy(t => t))
        {
            int idx = NearestAvailable(candTimes, claimed, g, tol);
            if (idx >= 0)
                claimed[idx] = true;
        }

        for (int i = 0; i < known.Count; i++)
            (claimed[i] ? matched : unmatched).Add(known[i]);

        if (guessed.Count > 0)
        {
            var anyOffset = (timestampsAtAnyOffset ?? google).Select(t => t.UtcDateTime).Order().ToArray();
            foreach (var photo in guessed)
                (CouldBeAtAnyOffset(photo, anyOffset, tol) ? matched : unmatched).Add(photo);
        }

        return new MatchResult(matched, unmatched, skipped);
    }

    // onlyIphonePhotos as the target had it when the photos were matched.
    public static string WhySkipped(PhotoFile photo, bool onlyIphonePhotos = true) =>
        photo.Timestamp is null ? (PhotoScanner.IsVideo(photo.Path) ? "No recording start time" : "No capture time")
        : onlyIphonePhotos && !photo.IsIphone ? $"Not taken with an iPhone{(photo.Camera is { } camera ? $" ({camera})" : "")}"
        : "No time zone, and it could be from just outside the range";

    // The photo's clock time is where it was taken; a Google photo is it in some
    // time zone when the two are a whole number of 15-minute steps apart, within
    // the range of real time zones, give or take the tolerance.
    private static bool CouldBeAtAnyOffset(PhotoFile photo, DateTime[] googleUtc, double tol)
    {
        // A guessed timestamp keeps the guessed offset, so its DateTime is the clock time.
        var clockTime = DateTime.SpecifyKind(photo.Timestamp!.Value.DateTime, DateTimeKind.Utc);
        var tolerance = TimeSpan.FromSeconds(tol);
        int i = LowerBound(googleUtc, clockTime - LatestOffset - tolerance);
        for (; i < googleUtc.Length && googleUtc[i] <= clockTime - EarliestOffset + tolerance; i++)
        {
            var offset = clockTime - googleUtc[i];
            var nearestStep = TimeSpan.FromTicks((long)Math.Round((double)offset.Ticks / OffsetStep.Ticks) * OffsetStep.Ticks);
            if ((offset - nearestStep).Duration() <= tolerance)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Index of the unclaimed candidate whose timestamp is closest to
    /// <paramref name="target"/> and within <paramref name="tol"/> seconds,
    /// or -1 if none qualifies.
    /// </summary>
    private static int NearestAvailable(
        DateTimeOffset[] times, bool[] claimed, DateTimeOffset target, double tol)
    {
        // First candidate with time >= target - tol.
        int lo = LowerBound(times, target.AddSeconds(-tol));

        int best = -1;
        double bestDiff = double.MaxValue;
        for (int i = lo; i < times.Length; i++)
        {
            double diff = (times[i] - target).TotalSeconds;
            if (diff > tol) break;               // past the tolerance window
            if (claimed[i]) continue;
            double abs = Math.Abs(diff);
            if (abs < bestDiff) { bestDiff = abs; best = i; }
        }
        return best;
    }

    // First index i where times[i] >= value (times is ascending).
    private static int LowerBound<T>(T[] times, T value) where T : IComparable<T>
    {
        int lo = 0, hi = times.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (times[mid].CompareTo(value) < 0) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
