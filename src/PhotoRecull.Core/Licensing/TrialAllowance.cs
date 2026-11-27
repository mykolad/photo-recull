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
    /// The cleanup the trial's free stage and delete went to, if one has used it: the first
    /// cleanup that ever ran.
    /// </summary>
    public static Cleanup? FreeCleanup(IEnumerable<Cleanup> cleanups) =>
        cleanups.Where(c => c.HasEverRun).OrderBy(c => c.PickedAt).FirstOrDefault();

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
