using PhotoRecull.Core.Library;

namespace PhotoRecull.Tests;

public class CleanupNeighborsTests
{
    private static Cleanup CleanupFor(DateOnly from, DateOnly to) =>
        new(Guid.NewGuid(), from, to, CleanupState.Done, PickedCount: 1,
            PickedAt: DateTimeOffset.UtcNow, LastActivityAt: DateTimeOffset.UtcNow);

    private readonly Cleanup _may = CleanupFor(new(2026, 5, 1), new(2026, 5, 31));
    private readonly Cleanup _july = CleanupFor(new(2026, 7, 1), new(2026, 7, 31));
    private readonly Cleanup _september = CleanupFor(new(2026, 9, 1), new(2026, 9, 30));

    [Fact]
    public void NewCleanup_ContinuesAfterTheLatestCleanup()
    {
        var neighbors = CleanupNeighbors.For([_july, _may, _september], current: null);

        Assert.Equal(new DateOnly(2026, 9, 30), neighbors.PreviousCleanupEnd);
        Assert.Null(neighbors.NextCleanupStart);
        Assert.Equal(new DateOnly(2026, 10, 1), neighbors.EarliestAllowedStart);
    }

    [Fact]
    public void ExistingCleanup_IsBoundedByTheCleanupsBeforeAndAfterIt()
    {
        var neighbors = CleanupNeighbors.For([_may, _july, _september], _july);

        Assert.Equal(new DateOnly(2026, 5, 31), neighbors.PreviousCleanupEnd);
        Assert.Equal(new DateOnly(2026, 9, 1), neighbors.NextCleanupStart);
        Assert.Equal(new DateOnly(2026, 8, 31), neighbors.LatestAllowedEnd);
    }

    [Fact]
    public void NoOtherCleanups_NoBounds()
    {
        var neighbors = CleanupNeighbors.For([_july], _july);

        Assert.Null(neighbors.EarliestAllowedStart);
        Assert.Null(neighbors.LatestAllowedEnd);
    }
}
