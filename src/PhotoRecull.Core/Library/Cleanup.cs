using PhotoRecull.Core.Settings;

namespace PhotoRecull.Core.Library;

public enum CleanupState
{
    Picked,
    ReadyToAct,
    Staged,
    Done,
}

public record Cleanup(
    Guid Id,
    DateOnly From,
    DateOnly To,
    CleanupState State,
    int PickedCount,
    DateTimeOffset PickedAt,
    DateTimeOffset LastActivityAt)
{
    public List<TargetSettings>? Targets { get; init; }
    public DateOnly? EarliestPickedDay { get; init; }
    public DateOnly? LatestPickedDay { get; init; }
    public int? ToRemoveCount { get; init; }
    public int KeptByUserCount { get; init; }
    public int StagedCount { get; init; }
    public int RemovedCount { get; init; }
    public long FreedBytes { get; init; }
}
