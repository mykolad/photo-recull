using PhotoRecull.Core.Library;

namespace PhotoRecull.Tests;

public class RangeChecksTests
{
    private static readonly DateOnly EarliestPicked = new(2026, 8, 2);
    private static readonly DateOnly LatestPicked = new(2026, 9, 25);

    private static Cleanup CleanupFor(DateOnly from, DateOnly to) =>
        new(Guid.NewGuid(), from, to, CleanupState.Done, PickedCount: 1,
            PickedAt: DateTimeOffset.UtcNow, LastActivityAt: DateTimeOffset.UtcNow);

    [Fact]
    public void DaysOutsideThePick_DesignExample()
    {
        var outside = RangeChecks.DaysOutsideThePick(new(2026, 8, 1), new(2026, 9, 30), EarliestPicked, LatestPicked);

        Assert.Equal(
        [
            new DayRange(new(2026, 8, 1), new(2026, 8, 1)),
            new DayRange(new(2026, 9, 26), new(2026, 9, 30)),
        ], outside);
    }

    [Fact]
    public void DaysOutsideThePick_RangeWithinThePick_None()
    {
        Assert.Empty(RangeChecks.DaysOutsideThePick(new(2026, 8, 10), new(2026, 9, 1), EarliestPicked, LatestPicked));
    }

    [Fact]
    public void DaysOutsideThePick_RangeEntirelyBeforeThePick_IsOneRange()
    {
        Assert.Equal([new DayRange(new(2026, 7, 1), new(2026, 7, 31))],
            RangeChecks.DaysOutsideThePick(new(2026, 7, 1), new(2026, 7, 31), EarliestPicked, LatestPicked));
    }

    [Theory]
    [InlineData("2026-08-02", "2026-09-25", false)]
    [InlineData("2026-08-01", "2026-09-30", false)]
    [InlineData("2026-08-03", "2026-09-25", true)]
    [InlineData("2026-08-02", "2026-09-24", true)]
    public void LeavesSomePickedPhotosOutside(string from, string to, bool expected)
    {
        Assert.Equal(expected,
            RangeChecks.LeavesSomePickedPhotosOutside(DateOnly.Parse(from), DateOnly.Parse(to), EarliestPicked, LatestPicked));
    }

    [Fact]
    public void OverlappingCleanups_IgnoresTheCurrentCleanupAndNonOverlapping()
    {
        var july = CleanupFor(new(2026, 7, 1), new(2026, 7, 31));
        var mid = CleanupFor(new(2026, 7, 20), new(2026, 8, 5));
        var current = CleanupFor(new(2026, 8, 1), new(2026, 8, 31));

        var overlapping = RangeChecks.OverlappingCleanups([july, mid, current], current.Id, new(2026, 8, 1), new(2026, 8, 31));

        Assert.Equal([mid], overlapping);
    }
}
