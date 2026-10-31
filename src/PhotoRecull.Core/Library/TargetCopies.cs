namespace PhotoRecull.Core.Library;

// A photo backed up to OneDrive and to Synology Photos is one photo with a copy on
// each, not a burst of two look-alikes. Copies are recognised by capture time:
// each target's file carries the same moment, read from its name or its metadata.
public static class TargetCopies
{
    // Two times to the millisecond agree to the millisecond, give or take rounding.
    private static readonly TimeSpan PreciseTolerance = TimeSpan.FromMilliseconds(10);

    // Groups files into photos, each with at most one copy per target. The
    // closest times pair first, so a burst on both targets pairs frame by frame.
    // A copy that could be either of two photos is left on its own: a stray tile
    // is better than Keep reaching a different photo. Groups and the copies in
    // them keep the files' order.
    public static List<List<T>> Group<T>(IReadOnlyList<T> files, Func<T, PhotoFile> photoOf, Func<T, object> targetOf)
    {
        var timed = Enumerable.Range(0, files.Count)
            .Where(i => photoOf(files[i]).Timestamp.HasValue)
            .OrderBy(i => photoOf(files[i]).Timestamp)
            .ToList();
        var pairs = new List<(TimeSpan Gap, int First, int Second)>();
        for (int a = 0; a < timed.Count; a++)
        {
            for (int b = a + 1; b < timed.Count; b++)
            {
                var (first, second) = (photoOf(files[timed[a]]), photoOf(files[timed[b]]));
                var gap = second.Timestamp!.Value - first.Timestamp!.Value;
                if (gap >= TimeSpan.FromSeconds(1))
                    break;
                if (MayBeTheSameCapture(first, second)
                    && !Equals(targetOf(files[timed[a]]), targetOf(files[timed[b]]))
                    && PhotoScanner.IsVideo(first.Path) == PhotoScanner.IsVideo(second.Path))
                    pairs.Add((gap, timed[a], timed[b]));
            }
        }

        // A whole-second time can't tell two captures within its second apart.
        var ambiguous = pairs
            .SelectMany(p => new[] { (Copy: p.First, Other: p.Second), (Copy: p.Second, Other: p.First) })
            .GroupBy(x => (x.Copy, Target: targetOf(files[x.Other])))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        bool IsAmbiguous(int copy, int other) => ambiguous.Contains((copy, targetOf(files[other])));

        var groupOf = Enumerable.Range(0, files.Count).ToArray();
        var members = files.Select((_, i) => new List<int> { i }).ToList();
        foreach (var (_, first, second) in pairs.Where(p => !IsAmbiguous(p.First, p.Second) && !IsAmbiguous(p.Second, p.First)).OrderBy(p => p.Gap))
        {
            var (kept, merged) = (groupOf[first], groupOf[second]);
            if (kept == merged
                || members[kept].Any(i => members[merged].Any(j => Equals(targetOf(files[i]), targetOf(files[j])))))
                continue;
            foreach (var i in members[merged])
                groupOf[i] = kept;
            members[kept].AddRange(members[merged]);
            members[merged].Clear();
        }

        return members
            .Where(group => group.Count > 0)
            .OrderBy(group => group.Min())
            .Select(group => group.Order().Select(i => files[i]).ToList())
            .ToList();
    }

    // first is the earlier. A time in whole seconds (a video's, or EXIF without
    // sub-seconds) stands for its whole second: the capture happened somewhere in
    // [t, t + 1 s), so a precise time matches only inside that second.
    private static bool MayBeTheSameCapture(PhotoFile first, PhotoFile second)
    {
        var gap = second.Timestamp!.Value - first.Timestamp!.Value;
        return (first.TimestampInWholeSeconds, second.TimestampInWholeSeconds) switch
        {
            (false, false) => gap <= PreciseTolerance,
            (true, true) => gap == TimeSpan.Zero,
            (true, false) => gap < TimeSpan.FromSeconds(1),
            (false, true) => gap == TimeSpan.Zero,
        };
    }
}
