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
    // is matched in two steps, so a wrong guess can only ever keep a photo:
    // - one-to-one at the guess, against the Google photos no photo with a known
    //   time zone has claimed, so bursts are culled as usual;
    // - then, if that found nothing, kept when any Google photo in
    //   timestampsAtAnyOffset (the whole pick, by default the same timestamps)
    //   could be it in another time zone.
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

        double tol = toleranceSeconds;
        var google = googleTimestamps.OrderBy(t => t).ToList();
        var googleClaimed = new bool[google.Count];

        foreach (var (candidate, isClaimed) in MatchOneToOne(known, google, googleClaimed, tol))
            (isClaimed ? matched : unmatched).Add(candidate);

        var anyOffset = (timestampsAtAnyOffset ?? google).Select(t => t.UtcDateTime).Order().ToArray();
        foreach (var (candidate, isClaimed) in MatchOneToOne(guessed, google, googleClaimed, tol))
            (isClaimed || CouldBeAtAnyOffset(candidate, anyOffset, tol) ? matched : unmatched).Add(candidate);

        return new MatchResult(matched, unmatched, skipped);
    }

    public static string WhySkipped(PhotoFile photo) =>
        photo.Timestamp is not null ? $"Not taken with an iPhone{(photo.Camera is { } camera ? $" ({camera})" : "")}"
        : PhotoScanner.IsVideo(photo.Path) ? "No recording start time"
        : "No capture time";

    // Each Google photo claims its nearest still-available candidate within
    // tolerance, Google photos in time order, so the assignment is stable; a
    // Google photo that claims one is marked so a later step can't claim it again.
    private static IEnumerable<(PhotoFile Candidate, bool IsClaimed)> MatchOneToOne(
        List<PhotoFile> candidates, List<DateTimeOffset> google, bool[] googleClaimed, double tol)
    {
        candidates.Sort((a, b) => a.Timestamp!.Value.CompareTo(b.Timestamp!.Value));
        var candTimes = candidates.Select(c => c.Timestamp!.Value).ToArray();
        var claimed = new bool[candidates.Count];
        for (int g = 0; g < google.Count; g++)
        {
            if (googleClaimed[g])
                continue;
            int idx = NearestAvailable(candTimes, claimed, google[g], tol);
            if (idx >= 0)
            {
                claimed[idx] = true;
                googleClaimed[g] = true;
            }
        }
        return candidates.Select((c, i) => (c, claimed[i]));
    }

    // The photo's clock time is where it was taken; a Google photo is it in some
    // time zone when the two are a whole number of 15-minute steps apart, within
    // the range of real time zones, give or take the tolerance. The guessed time
    // zone itself is left out: the one-to-one step already decided it, and taking
    // it again would keep every burst frame next to the one that was kept.
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
            if (nearestStep != photo.Timestamp.Value.Offset && (offset - nearestStep).Duration() <= tolerance)
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
