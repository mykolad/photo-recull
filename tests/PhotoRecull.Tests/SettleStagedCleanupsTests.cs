using Microsoft.Data.Sqlite;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;
using PhotoRecull.Core.Storage;

namespace PhotoRecull.Tests;

public class SettleStagedCleanupsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("photorecull-settle-").FullName;
    private readonly AppStateDatabase _database;

    public SettleStagedCleanupsTests()
    {
        _database = new AppStateDatabase(Path.Combine(_folder, "photorecull.db"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private string Target => Path.Combine(_folder, "Camera Roll");

    // A Staged cleanup whose one stage run moved a photo to stagedPath.
    private Cleanup StagedCleanup(string stageFolder, string stagedPath)
    {
        var cleanup = new Cleanup(Guid.NewGuid(), new(2026, 10, 1), new(2026, 10, 31), CleanupState.ReadyToAct,
            PickedCount: 5, PickedAt: DateTimeOffset.UnixEpoch, LastActivityAt: DateTimeOffset.UnixEpoch);
        _database.SaveCleanup(cleanup);
        var run = new CleanupRun(cleanup.Id, DateTimeOffset.UnixEpoch, CleanupAction.Staged, new TargetSettings("OneDrive", Target),
            stageFolder, 0, 0, 0);
        long id = _database.StartRun(run);
        _database.RecordRunFile(id, new RunFile(Path.Combine(Target, "IMG_1.HEIC"), stagedPath, null, null, Succeeded: true, null, 10));
        var staged = cleanup with { State = CleanupState.Staged, StagedCount = 1 };
        _database.CompleteRunsWithCleanup(staged, [run with { Id = id, SucceededCount = 1 }]);
        return staged;
    }

    private static string StagedFile(string stageFolder)
    {
        Directory.CreateDirectory(stageFolder);
        var path = Path.Combine(stageFolder, "IMG_1.HEIC");
        File.WriteAllText(path, "photo");
        return path;
    }

    private CleanupState StateOf(Cleanup cleanup) => _database.LoadCleanups().Single(c => c.Id == cleanup.Id).State;

    [Fact]
    public void AStagedCleanupWhosePhotosWereDeletedByHand_IsDone()
    {
        var stageFolder = Path.Combine(_folder, "_staged");
        var cleanup = StagedCleanup(stageFolder, StagedFile(stageFolder));
        Directory.Delete(stageFolder, recursive: true);

        var settled = StagedPhotos.SettleStagedCleanups(_database);

        Assert.Equal(cleanup.Id, Assert.Single(settled).Id);
        var saved = _database.LoadCleanups().Single(c => c.Id == cleanup.Id);
        Assert.Equal((CleanupState.Done, 0), (saved.State, saved.StagedCount));
    }

    [Fact]
    public void Settling_ChangesOnlyTheState_KeepingWhatEarlierRunsRemovedAndFreed()
    {
        var stageFolder = Path.Combine(_folder, "_staged");
        var cleanup = StagedCleanup(stageFolder, StagedFile(stageFolder));
        _database.SaveCleanup(cleanup with { RemovedCount = 3, FreedBytes = 4_000_000, KeptByUserCount = 1 });
        Directory.Delete(stageFolder, recursive: true);

        StagedPhotos.SettleStagedCleanups(_database);

        var saved = _database.LoadCleanups().Single(c => c.Id == cleanup.Id);
        Assert.Equal((CleanupState.Done, 3, 4_000_000L, 1), (saved.State, saved.RemovedCount, saved.FreedBytes, saved.KeptByUserCount));
    }

    [Fact]
    public void ACleanupThatStartedARunSinceItWasChecked_IsLeftAsItIs()
    {
        var stageFolder = Path.Combine(_folder, "_staged");
        var cleanup = StagedCleanup(stageFolder, StagedFile(stageFolder));
        long checkedUpTo = _database.LoadRunsNewestFirst().Max(r => r.Id);
        // Review staged starts moving the photos back while Library's check is running.
        _database.StartRun(new CleanupRun(cleanup.Id, DateTimeOffset.Now, CleanupAction.MovedBack,
            new TargetSettings("OneDrive", Target), Target, 0, 0, 0));

        Assert.False(_database.SettleStagedCleanup(cleanup.Id, checkedUpTo, DateTimeOffset.Now));
        Assert.Equal(CleanupState.Staged, StateOf(cleanup));
    }

    [Fact]
    public void ACleanupWithARunStillGoing_IsLeftAsItIs()
    {
        var stageFolder = Path.Combine(_folder, "_staged");
        var cleanup = StagedCleanup(stageFolder, StagedFile(stageFolder));
        Directory.Delete(stageFolder, recursive: true);
        // Staging again, before its first photo is recorded: its stage folder looks empty.
        _database.StartRun(new CleanupRun(cleanup.Id, DateTimeOffset.Now, CleanupAction.Staged,
            new TargetSettings("OneDrive", Target), stageFolder, 0, 0, 0));

        Assert.Empty(StagedPhotos.SettleStagedCleanups(_database));
        Assert.Equal(CleanupState.Staged, StateOf(cleanup));
    }

    [Fact]
    public void AStagedCleanupWithAPhotoStillStaged_StaysStaged()
    {
        var stageFolder = Path.Combine(_folder, "_staged");
        var cleanup = StagedCleanup(stageFolder, StagedFile(stageFolder));

        Assert.Empty(StagedPhotos.SettleStagedCleanups(_database));
        Assert.Equal(CleanupState.Staged, StateOf(cleanup));
    }

    [Fact]
    public void AStagedCleanupWhoseStageFolderCantBeReached_StaysStaged()
    {
        var stageFolder = @"\\nowhere-photorecull-test\share\_staged";
        var cleanup = StagedCleanup(stageFolder, Path.Combine(stageFolder, "IMG_1.HEIC"));

        Assert.Empty(StagedPhotos.SettleStagedCleanups(_database));
        Assert.Equal(CleanupState.Staged, StateOf(cleanup));
    }
}
