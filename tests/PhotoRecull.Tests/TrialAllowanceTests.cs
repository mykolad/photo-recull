using PhotoRecull.Core.Library;
using PhotoRecull.Core.Licensing;

namespace PhotoRecull.Tests;

public class TrialAllowanceTests
{
    private static Cleanup CleanupPickedOn(int day, bool hasEverRun) =>
        new(Guid.NewGuid(), new(2026, 10, 1), new(2026, 10, 31), CleanupState.ReadyToAct, PickedCount: 1,
            PickedAt: new DateTimeOffset(2026, 11, day, 12, 0, 0, TimeSpan.Zero), LastActivityAt: DateTimeOffset.UtcNow)
        {
            HasEverRun = hasEverRun,
        };

    [Fact]
    public void NoCleanupHasRun_AnyCleanupMayAct()
    {
        var first = CleanupPickedOn(1, hasEverRun: false);
        var second = CleanupPickedOn(2, hasEverRun: false);

        Assert.Null(TrialAllowance.FreeCleanup([first, second]));
        Assert.True(TrialAllowance.MayAct(first.Id, [first, second]));
        Assert.True(TrialAllowance.MayAct(second.Id, [first, second]));
    }

    [Fact]
    public void ANewCleanup_MayActWhenNoneExistYet()
    {
        Assert.True(TrialAllowance.MayAct(Guid.NewGuid(), []));
    }

    [Fact]
    public void TheCleanupThatRan_StaysFreeToFinish()
    {
        var free = CleanupPickedOn(1, hasEverRun: true);
        var other = CleanupPickedOn(2, hasEverRun: false);

        Assert.Equal(free, TrialAllowance.FreeCleanup([other, free]));
        Assert.True(TrialAllowance.MayAct(free.Id, [other, free]));
    }

    [Fact]
    public void OnceOneCleanupRan_OthersMayNotAct()
    {
        var free = CleanupPickedOn(5, hasEverRun: true);
        var older = CleanupPickedOn(1, hasEverRun: false);
        var newer = CleanupPickedOn(9, hasEverRun: false);

        Assert.False(TrialAllowance.MayAct(older.Id, [older, free, newer]));
        Assert.False(TrialAllowance.MayAct(newer.Id, [older, free, newer]));
        Assert.False(TrialAllowance.MayAct(Guid.NewGuid(), [older, free, newer]));
    }

    [Fact]
    public void SeveralCleanupsRan_TheEarliestPickedIsTheFreeOne()
    {
        // A purchase that was refunded leaves several that ran; only one stays free.
        var later = CleanupPickedOn(8, hasEverRun: true);
        var earlier = CleanupPickedOn(3, hasEverRun: true);

        Assert.Equal(earlier, TrialAllowance.FreeCleanup([later, earlier]));
        Assert.False(TrialAllowance.MayAct(later.Id, [later, earlier]));
    }
}
