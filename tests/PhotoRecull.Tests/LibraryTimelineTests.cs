using PhotoRecull.Core.Library;

namespace PhotoRecull.Tests;

public class LibraryTimelineTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    private static Cleanup CleanupFor(DateOnly from, DateOnly to, CleanupState state = CleanupState.Done) =>
        new(Guid.NewGuid(), from, to, state, PickedCount: 10,
            PickedAt: DateTimeOffset.UtcNow, LastActivityAt: DateTimeOffset.UtcNow);

    [Fact]
    public void NoCleanups_NoGaps()
    {
        Assert.Empty(LibraryTimeline.FindNotCleanedGaps([], Today));
    }

    [Fact]
    public void GapBetweenCleanupsAndUntilToday_DaysBeforeFirstCleanupAreNotAGap()
    {
        var march = CleanupFor(new(2026, 3, 1), new(2026, 3, 31));
        var may = CleanupFor(new(2026, 5, 1), new(2026, 5, 31));

        var gaps = LibraryTimeline.FindNotCleanedGaps([may, march], Today);

        Assert.Equal(
        [
            new NotCleanedGap(new(2026, 4, 1), new(2026, 4, 30)),
            new NotCleanedGap(new(2026, 6, 1), Today),
        ], gaps);
    }

    [Fact]
    public void AdjacentAndOverlappingCleanups_LeaveNoGap()
    {
        var first = CleanupFor(new(2026, 1, 1), new(2026, 1, 31));
        var adjacent = CleanupFor(new(2026, 2, 1), new(2026, 2, 28));
        var overlapping = CleanupFor(new(2026, 2, 15), new(2026, 3, 10));
        var containedInFirst = CleanupFor(new(2026, 1, 5), new(2026, 1, 6));

        var gaps = LibraryTimeline.FindNotCleanedGaps([first, adjacent, overlapping, containedInFirst], Today);

        Assert.Equal([new NotCleanedGap(new(2026, 3, 11), Today)], gaps);
    }

    [Fact]
    public void CleanupEndingToday_LeavesNoGapUntilToday()
    {
        Assert.Empty(LibraryTimeline.FindNotCleanedGaps([CleanupFor(new(2026, 10, 1), Today)], Today));
    }

    [Fact]
    public void CleanupsAndGapsNewestFirst_InterleavesGapsByDate()
    {
        var march = CleanupFor(new(2026, 3, 1), new(2026, 3, 31));
        var may = CleanupFor(new(2026, 5, 1), new(2026, 5, 31), CleanupState.ReadyToAct);

        var entries = LibraryTimeline.CleanupsAndGapsNewestFirst([march, may], Today);

        Assert.Collection(entries,
            e => Assert.Equal(new NotCleanedGap(new(2026, 6, 1), Today), e),
            e => Assert.Equal(may, Assert.IsType<CleanupEntry>(e).Cleanup),
            e => Assert.Equal(new NotCleanedGap(new(2026, 4, 1), new(2026, 4, 30)), e),
            e => Assert.Equal(march, Assert.IsType<CleanupEntry>(e).Cleanup));
    }

    [Fact]
    public void DayCount_IncludesBothEnds()
    {
        Assert.Equal(26, new NotCleanedGap(new(2026, 7, 6), new(2026, 7, 31)).DayCount);
    }
}
