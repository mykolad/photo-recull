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

    // Whether a stage or delete ever ran for it; read from its runs, not saved.
    public bool HasRuns { get; init; }

    // Only a cleanup that has never staged or deleted: once a run has started,
    // files may have moved even if no photo completed (a Live Photo's video
    // went, then its photo failed), there's no going back to before, and History
    // keeps what happened.
    public bool CanBeDiscarded => State is CleanupState.Picked or CleanupState.ReadyToAct
        && StagedCount == 0 && RemovedCount == 0 && !HasRuns;
}
