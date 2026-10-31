using Microsoft.Data.Sqlite;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;
using PhotoRecull.Core.Storage;

namespace PhotoRecull.Tests;

public class RunJournalTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("photorecull-journal-").FullName;
    private readonly AppStateDatabase _database;
    private readonly Cleanup _cleanup = new(Guid.NewGuid(), new(2026, 10, 1), new(2026, 10, 31), CleanupState.ReadyToAct,
        PickedCount: 5, PickedAt: DateTimeOffset.UnixEpoch, LastActivityAt: DateTimeOffset.UnixEpoch);
    private static readonly TargetSettings OneDrive = new(TargetKind.OneDrive, @"C:\Camera Roll");

    public RunJournalTests()
    {
        _database = new AppStateDatabase(Path.Combine(_folder, "photorecull.db"));
        _database.SaveCleanup(_cleanup);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private CleanupRun RunOf(CleanupAction action, string? destination = null) =>
        new(_cleanup.Id, new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), action, OneDrive, destination, 0, 0, 0);

    private static RunFile Moved(string name, long bytes) =>
        new($@"C:\Camera Roll\{name}", $@"C:\_staged\{name}", null, null, Succeeded: true, null, bytes);

    [Fact]
    public void ARunInProgress_IsSavedBeforeItsPhotos_AndCompletedWithTheCleanup()
    {
        var run = RunOf(CleanupAction.Staged, @"C:\_staged");
        long id = _database.StartRun(run);
        Assert.Equal(RunStatus.InProgress, Assert.Single(_database.LoadRunsNewestFirst()).Status);
        Assert.True(Assert.Single(_database.LoadCleanups()).HasEverRun);

        _database.RecordRunFile(id, Moved("a.heic", 100));
        _database.RecordRunFile(id, Moved("b.heic", 200));
        _database.CompleteRunsWithCleanup(_cleanup with { State = CleanupState.Staged, StagedCount = 2 },
            [run with { Id = id, SucceededCount = 2 }]);

        var saved = Assert.Single(_database.LoadRunsNewestFirst());
        Assert.Equal((id, RunStatus.Complete, 2), (saved.Id, saved.Status, saved.SucceededCount));
        Assert.True(saved.PhotosRecorded);
        Assert.Equal([Moved("a.heic", 100), Moved("b.heic", 200)], _database.LoadRunFiles(id));
        Assert.Equal(CleanupState.Staged, Assert.Single(_database.LoadCleanups()).State);
    }

    [Fact]
    public void ARunCutOff_IsFinishedAtTheNextStartFromThePhotosItRecorded()
    {
        _database.SaveCleanup(_cleanup with { ToRemoveCount = 4 });
        long staged = _database.StartRun(RunOf(CleanupAction.Staged, @"C:\_staged"));
        _database.RecordRunFile(staged, Moved("a.heic", 100));
        _database.RecordRunFile(staged, new(@"C:\Camera Roll\b.heic", null, null, null, Succeeded: false, "Locked", 0));
        long deleted = _database.StartRun(RunOf(CleanupAction.Deleted));
        _database.RecordRunFile(deleted, new(@"C:\Camera Roll\c.heic", null, null, null, Succeeded: true, null, 3_000));
        // A Live Photo whose video was deleted before its photo failed.
        _database.RecordRunFile(deleted, new(@"C:\Camera Roll\d.heic", null, @"C:\Camera Roll\d.mov", null, Succeeded: false, "Locked", 500));

        Assert.Equal(2, _database.FinishInterruptedRuns());

        var runs = _database.LoadRunsNewestFirst().ToDictionary(r => r.Id);
        Assert.Equal((RunStatus.Interrupted, 1, 1, 0L), (runs[staged].Status, runs[staged].SucceededCount, runs[staged].FailedCount, runs[staged].FreedBytes));
        Assert.Equal((RunStatus.Interrupted, 1, 1, 3_500L), (runs[deleted].Status, runs[deleted].SucceededCount, runs[deleted].FailedCount, runs[deleted].FreedBytes));
        var cleanup = Assert.Single(_database.LoadCleanups());
        Assert.Equal((CleanupState.ReadyToAct, 1, 1, 3_500L, (int?)2),
            (cleanup.State, cleanup.StagedCount, cleanup.RemovedCount, cleanup.FreedBytes, cleanup.ToRemoveCount));
        Assert.Equal(0, _database.FinishInterruptedRuns());
    }

    [Fact]
    public void ADeleteRemembersWhetherItWentToTheRecycleBin()
    {
        _database.StartRun(RunOf(CleanupAction.Deleted) with { WentToRecycleBin = true });
        _database.StartRun(RunOf(CleanupAction.Staged, @"C:\_staged"));

        var wentToRecycleBin = _database.LoadRunsNewestFirst().ToDictionary(r => r.Action, r => r.WentToRecycleBin);
        Assert.Equal(true, wentToRecycleBin[CleanupAction.Deleted]);
        Assert.Null(wentToRecycleBin[CleanupAction.Staged]);
    }

    [Fact]
    public void AMoveBackCutOff_KeepsWhatWentBack_AndDoesntCountItAsRemoved()
    {
        _database.SaveCleanup(_cleanup with { ToRemoveCount = 3 });
        long id = _database.StartRun(RunOf(CleanupAction.MovedBack, @"C:\Camera Roll"));
        _database.RecordRunFile(id, new(@"C:\_staged\a.heic", @"C:\Camera Roll\a.heic", null, null, true, null, 100), keptFor: _cleanup.Id);
        _database.RecordRunFile(id, new(@"C:\_staged\b.heic", null, null, null, false, "Locked", 0), keptFor: _cleanup.Id);

        _database.FinishInterruptedRuns();

        Assert.Equal([@"C:\Camera Roll\a.heic"], _database.LoadKeptPhotoPaths(_cleanup.Id));
        var cleanup = Assert.Single(_database.LoadCleanups());
        Assert.Equal((1, (int?)3), (cleanup.KeptByUserCount, cleanup.ToRemoveCount));
    }

    [Fact]
    public void RemovingTheLastHistoryEntry_StillDoesntLetACleanupThatRanBeDiscarded()
    {
        // A Live Photo's video was staged, then its photo failed: no photo counts.
        long id = _database.StartRun(RunOf(CleanupAction.Staged, @"C:\_staged"));
        _database.RecordRunFile(id, new(@"C:\Camera Roll\a.heic", null, @"C:\Camera Roll\a.mov", @"C:\_staged\a.mov", false, "Locked", 300));
        _database.CompleteRunsWithCleanup(_cleanup, [RunOf(CleanupAction.Staged, @"C:\_staged") with { Id = id, FailedCount = 1 }]);

        _database.RemoveRun(id);

        var cleanup = Assert.Single(_database.LoadCleanups());
        Assert.True(cleanup.HasEverRun);
        Assert.False(cleanup.CanBeDiscarded);
        Assert.False(_database.DiscardCleanup(_cleanup.Id));
    }

    [Fact]
    public void RemovingARun_ForgetsItAndItsPhotos_ButNotTheCleanup()
    {
        long id = _database.StartRun(RunOf(CleanupAction.Staged, @"C:\_staged"));
        _database.RecordRunFile(id, Moved("a.heic", 100));

        _database.RemoveRun(id);

        Assert.Empty(_database.LoadRunsNewestFirst());
        Assert.Empty(_database.LoadRunFiles(id));
        Assert.Single(_database.LoadCleanups());
    }
}
