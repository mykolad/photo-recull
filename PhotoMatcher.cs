namespace PhotoCleanup;

public record MatchResult(
    List<OneDrivePhoto> Matched,
    List<OneDrivePhoto> Unmatched,
    List<OneDrivePhoto> Skipped    // non-iPhone or no timestamp
);

/// <summary>
/// Matches OneDrive photos against the timestamps of photos currently in
/// Google Photos.
///
/// Matching is ONE-TO-ONE: each Google photo can keep at most one OneDrive
/// file. This is essential for bursts — if you shot several frames within a
/// couple of seconds and later kept only one in Google Photos, only the
/// OneDrive frame nearest that Google timestamp is kept; the others become
/// removal candidates. A naive "any file within tolerance matches" would keep
/// the whole burst.
///
/// Google timestamps are supplied as a sequence (not a set) so duplicates are
/// preserved: if two kept frames share the same whole-second time, both
/// OneDrive files are kept.
/// </summary>
public static class PhotoMatcher
{
    public static MatchResult Match(
        IEnumerable<DateTimeOffset> googleTimestamps,
        List<OneDrivePhoto> oneDrivePhotos,
        int toleranceSeconds = 2)
    {
        var matched   = new List<OneDrivePhoto>();
        var unmatched = new List<OneDrivePhoto>();
        var skipped   = new List<OneDrivePhoto>();

        // iPhone photos with a usable timestamp are match candidates;
        // everything else is skipped outright.
        var candidates = new List<OneDrivePhoto>();
        foreach (var p in oneDrivePhotos)
        {
            if (p.IsIphone && p.Timestamp.HasValue)
                candidates.Add(p);
            else
                skipped.Add(p);
        }

        // Sort candidates by time for binary search + stable tie-breaking.
        candidates.Sort((a, b) => a.Timestamp!.Value.CompareTo(b.Timestamp!.Value));
        var candTimes = candidates.Select(c => c.Timestamp!.Value).ToArray();
        var claimed = new bool[candidates.Count];

        double tol = toleranceSeconds;

        // Each Google photo claims its nearest still-available OneDrive file
        // within tolerance. Processing Google timestamps in order keeps the
        // assignment stable.
        foreach (var g in googleTimestamps.OrderBy(t => t))
        {
            int idx = NearestAvailable(candTimes, claimed, g, tol);
            if (idx >= 0)
                claimed[idx] = true;
        }

        for (int i = 0; i < candidates.Count; i++)
            (claimed[i] ? matched : unmatched).Add(candidates[i]);

        return new MatchResult(matched, unmatched, skipped);
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
    private static int LowerBound(DateTimeOffset[] times, DateTimeOffset value)
    {
        int lo = 0, hi = times.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (times[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
