using PhotoRecull.Core.Settings;

namespace PhotoRecull.Core.Library;

public enum CleanupAction
{
    Staged,
    Deleted,
}

public enum RunStatus
{
    Complete,
    // Saved before the first file is touched, so a crash or power loss leaves a
    // record of the run and of each photo done so far.
    InProgress,
    // Found in progress at the next start, and finished from its recorded photos.
    Interrupted,
}

// One stage or delete of a cleanup's candidates on one target.
public record CleanupRun(
    Guid CleanupId,
    DateTimeOffset RanAt,
    CleanupAction Action,
    TargetSettings Target,
    string? Destination,
    int SucceededCount,
    int FailedCount,
    long FreedBytes)
{
    public long Id { get; init; }
    public RunStatus Status { get; init; } = RunStatus.Complete;
}

// One photo of a run: where it was, where it went (for a stage), and how it went.
// A Live Photo's video goes with it; Bytes counts what actually moved or was
// deleted, so a video that went before its photo failed counts too.
public sealed record RunFile(
    string Path,
    string? Destination,
    string? VideoPath,
    string? VideoDestination,
    bool Succeeded,
    string? FailureReason,
    long Bytes);
