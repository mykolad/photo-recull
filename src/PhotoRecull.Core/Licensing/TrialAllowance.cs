using PhotoRecull.Core.Library;

namespace PhotoRecull.Core.Licensing;

/// <summary>
/// What the Microsoft Store build's free trial allows. Picking, scanning and reviewing are
/// always free; staging and deleting are free in one cleanup, the first one a stage or delete
/// starts in. That cleanup stays free to finish: deleting what it staged, moving photos back,
/// retrying a failed run.
/// </summary>
public static class TrialAllowance
{
    /// <summary>
    /// The cleanup the trial's free stage and delete went to, if one has used it: the cleanup
    /// whose first stage or delete started first. Picking again doesn't change it, and nor
    /// do later runs after a purchase that was refunded.
    /// </summary>
    public static Cleanup? FreeCleanup(IEnumerable<Cleanup> cleanups) =>
        cleanups
            .Where(c => c.HasEverRun)
            // A cleanup that ran before first runs were saved, and has no runs left in
            // History, has no time: it comes after those that do, oldest pick first.
            .OrderBy(c => c.FirstRunAt ?? DateTimeOffset.MaxValue)
            .ThenBy(c => c.PickedAt)
            .FirstOrDefault();

    /// <summary>
    /// Whether a trial may stage or delete in <paramref name="cleanupId"/>: when no cleanup has
    /// run yet (this one becomes the free one), or this one is the free one.
    /// </summary>
    public static bool MayAct(Guid cleanupId, IEnumerable<Cleanup> cleanups)
    {
        var free = FreeCleanup(cleanups);
        return free is null || free.Id == cleanupId;
    }
}
