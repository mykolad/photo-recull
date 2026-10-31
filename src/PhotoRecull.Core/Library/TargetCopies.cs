namespace PhotoRecull.Core.Library;

// A photo backed up to OneDrive and to Synology Photos is one photo with a copy on
// each, not a burst of two look-alikes. Copies are recognised by capture time:
// each target's file carries the same moment, read from its name or its metadata.
public static class TargetCopies
{
    // Two times to the millisecond agree to the millisecond. A time in whole
    // seconds (a video's, or EXIF without sub-seconds) can be up to a second off,
    // which is still far apart from the next photo of a burst only when that
    // photo's time is precise too.
    private static readonly TimeSpan PreciseTolerance = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan WholeSecondTolerance = TimeSpan.FromSeconds(1);

    // Groups files into photos, each with at most one copy per target. The
    // closest times pair first, so a burst on both targets pairs frame by frame.
    // Groups and the copies in them keep the files' order.
    public static List<List<T>> Group<T>(IReadOnlyList<T> files, Func<T, PhotoFile> photoOf, Func<T, object> targetOf)
    {
        var groupOf = Enumerable.Range(0, files.Count).ToArray();
        var members = files.Select((_, i) => new List<int> { i }).ToList();

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
                if (gap > WholeSecondTolerance)
                    break;
                if (gap <= ToleranceBetween(first, second)
                    && !Equals(targetOf(files[timed[a]]), targetOf(files[timed[b]]))
                    && PhotoScanner.IsVideo(first.Path) == PhotoScanner.IsVideo(second.Path))
                    pairs.Add((gap, timed[a], timed[b]));
            }
        }

        foreach (var (_, first, second) in pairs.OrderBy(p => p.Gap))
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

    private static TimeSpan ToleranceBetween(PhotoFile first, PhotoFile second) =>
        first.TimestampInWholeSeconds || second.TimestampInWholeSeconds ? WholeSecondTolerance : PreciseTolerance;
}
