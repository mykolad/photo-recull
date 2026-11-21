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
