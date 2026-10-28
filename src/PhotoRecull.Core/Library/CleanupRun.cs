using PhotoRecull.Core.Settings;

namespace PhotoRecull.Core.Library;

public enum CleanupAction
{
    Staged,
    Deleted,
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
    long FreedBytes);
