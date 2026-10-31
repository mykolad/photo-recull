using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Data.Sqlite;
using PhotoRecull.Core.Library;
using PhotoRecull.Core.Settings;
using PhotoRecull.Core.Storage;

namespace PhotoRecull.Tests;

public class AppStateDatabaseTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("photorecull-db-").FullName;

    private string DatabasePath => Path.Combine(_folder, "photorecull.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private static Cleanup CleanupFor(DateOnly from, DateOnly to, CleanupState state = CleanupState.Picked) =>
        new(Guid.NewGuid(), from, to, state, PickedCount: 3,
            PickedAt: new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.FromHours(3)),
            LastActivityAt: new DateTimeOffset(2026, 8, 2, 10, 5, 0, TimeSpan.FromHours(3)));

    [Fact]
    public void FirstLaunch_CreatesTheDatabaseAndItsFolder_WithDefaults()
    {
        var database = new AppStateDatabase(Path.Combine(_folder, "not-created-yet", "photorecull.db"));

        Assert.True(File.Exists(database.DatabasePath));
        Assert.Null(database.LoadSettings().SetupCompletedAt);
        Assert.Empty(database.LoadSettings().Targets);
        Assert.Empty(database.LoadCleanups());
        Assert.False(database.HasGoogleToken());
    }

    [Fact]
    public void Settings_SurviveReopeningTheDatabase()
    {
        var saved = new AppSettings
        {
            SignInBrowser = "chrome",
            Targets = [new TargetSettings(TargetKind.OneDrive, @"C:\Users\me\OneDrive\Pictures\Camera Roll")],
            ShowCaptureTimesInUtc = true,
            ReviewFilter = ReviewFilter.OnlyToRemove,
            SetupCompletedAt = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromHours(2)),
        };

        new AppStateDatabase(DatabasePath).SaveSettings(saved);
        var loaded = new AppStateDatabase(DatabasePath).LoadSettings();

        Assert.Equal(saved.SignInBrowser, loaded.SignInBrowser);
        Assert.Equal(saved.Targets, loaded.Targets);
        Assert.True(loaded.ShowCaptureTimesInUtc);
        Assert.Equal(ReviewFilter.OnlyToRemove, loaded.ReviewFilter);
        Assert.Null(loaded.DeveloperCredentialsPath);
        Assert.Equal(saved.SetupCompletedAt, loaded.SetupCompletedAt);
    }

    [Fact]
    public void Settings_ClearingAValueRemovesIt()
    {
        var database = new AppStateDatabase(DatabasePath);
        database.SaveSettings(new AppSettings { SignInBrowser = "edge" });

        database.SaveSettings(new AppSettings { SignInBrowser = null });

        Assert.Null(database.LoadSettings().SignInBrowser);
    }

    [Fact]
    public void Cleanups_AreUpsertedAndListedByDate()
    {
        var database = new AppStateDatabase(DatabasePath);
        var august = CleanupFor(new(2026, 8, 1), new(2026, 8, 31));
        var july = CleanupFor(new(2026, 7, 1), new(2026, 7, 31)) with
        {
            Targets = [new TargetSettings(TargetKind.OneDrive, @"D:\Camera Roll")],
            EarliestPickedDay = new(2026, 7, 2),
            LatestPickedDay = new(2026, 7, 30),
        };

        database.SaveCleanup(august);
        database.SaveCleanup(july);
        var readyAugust = august with { State = CleanupState.ReadyToAct, ToRemoveCount = 41, FreedBytes = 5_000_000_000 };
        database.SaveCleanup(readyAugust);

        var cleanups = new AppStateDatabase(DatabasePath).LoadCleanups();
        Assert.Equal([july.Id, august.Id], cleanups.Select(c => c.Id));
        Assert.Equal(readyAugust, cleanups[1]);
        Assert.Equal(july.Targets, cleanups[0].Targets);
        Assert.Equal(july with { Targets = null }, cleanups[0] with { Targets = null });
    }

    [Fact]
    public void Pick_IsSavedWithItsCleanupAndKeepsDuplicateTimes()
    {
        var database = new AppStateDatabase(DatabasePath);
        var burstTime = new DateTimeOffset(2026, 8, 2, 7, 33, 33, 208, TimeSpan.Zero);
        var cleanup = CleanupFor(new(2026, 8, 1), new(2026, 8, 31)) with
        {
            EarliestPickedDay = new(2026, 8, 2),
            LatestPickedDay = new(2026, 8, 2),
        };

        database.SavePickWithCleanup(cleanup, [burstTime, burstTime, burstTime.AddSeconds(5)]);

        var pick = new AppStateDatabase(DatabasePath).LoadPick(cleanup.Id);
        Assert.NotNull(pick);
        Assert.Equal([burstTime, burstTime, burstTime.AddSeconds(5)], pick.Timestamps.Order());
        Assert.Equal(new DateOnly(2026, 8, 2), pick.From);
        Assert.Equal(cleanup, Assert.Single(database.LoadCleanups()));
    }

    [Fact]
    public void Pick_SavedAgainReplacesThePreviousPick()
    {
        var database = new AppStateDatabase(DatabasePath);
        var cleanup = CleanupFor(new(2026, 8, 1), new(2026, 8, 31));
        database.SavePickWithCleanup(cleanup, [DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)]);

        database.SavePickWithCleanup(cleanup with { PickedCount = 1 }, [DateTimeOffset.UnixEpoch.AddDays(2)]);

        Assert.Equal([DateTimeOffset.UnixEpoch.AddDays(2)], database.LoadPick(cleanup.Id)!.Timestamps);
        Assert.Equal(1, Assert.Single(database.LoadCleanups()).PickedCount);
    }

    [Fact]
    public void Pick_SavedAgainForgetsThePhotosKeptInThePreviousReview()
    {
        var database = new AppStateDatabase(DatabasePath);
        var cleanup = CleanupFor(new(2026, 8, 1), new(2026, 8, 31));
        var other = CleanupFor(new(2026, 9, 1), new(2026, 9, 30));
        database.SavePickWithCleanup(cleanup, [DateTimeOffset.UnixEpoch]);
        database.SavePickWithCleanup(other, [DateTimeOffset.UnixEpoch]);
        database.SetPhotoKept(cleanup.Id, @"C:\Camera Roll\IMG_1.HEIC", isKept: true);
        database.SetPhotoKept(other.Id, @"C:\Camera Roll\IMG_9.HEIC", isKept: true);

        database.SavePickWithCleanup(cleanup, [DateTimeOffset.UnixEpoch.AddDays(1)]);

        Assert.Empty(database.LoadKeptPhotoPaths(cleanup.Id));
        Assert.Single(database.LoadKeptPhotoPaths(other.Id));
    }

    [Fact]
    public void Pick_MissingForACleanup_IsNull()
    {
        Assert.Null(new AppStateDatabase(DatabasePath).LoadPick(Guid.NewGuid()));
    }

    [Fact]
    public void DamagedDatabaseFile_IsSetAsideAndAFreshOneCreated()
    {
        File.WriteAllText(DatabasePath, "this is not a SQLite database, just some damaged bytes");

        var database = new AppStateDatabase(DatabasePath);

        Assert.Empty(database.LoadCleanups());
        var keptAside = Assert.Single(Directory.GetFiles(_folder, "photorecull.db.damaged-*"));
        Assert.Equal("this is not a SQLite database, just some damaged bytes", File.ReadAllText(keptAside));
    }

    [Fact]
    public void DatabaseWithADamagedTablePage_IsSetAsideAtStartupInsteadOfFailingLater()
    {
        var original = new AppStateDatabase(DatabasePath);
        for (int i = 0; i < 300; i++)
            original.SaveCleanup(CleanupFor(new DateOnly(2020, 1, 1).AddDays(i), new DateOnly(2020, 1, 1).AddDays(i)));
        SqliteConnection.ClearAllPools();

        // Keep the first page (header and schema) intact and scramble the table
        // pages after it, so the file still opens as a database.
        var bytes = File.ReadAllBytes(DatabasePath);
        const int pageSize = 4096;
        Assert.True(bytes.Length > 4 * pageSize);
        for (int i = pageSize; i < bytes.Length; i++)
            bytes[i] = (byte)(i * 31 % 251);
        File.WriteAllBytes(DatabasePath, bytes);

        var reopened = new AppStateDatabase(DatabasePath);

        Assert.Empty(reopened.LoadCleanups());
        Assert.Null(reopened.LoadSettings().SetupCompletedAt);
        Assert.Single(Directory.GetFiles(_folder, "photorecull.db.damaged-*"));
    }

    [Fact]
    public void KeptPhotos_AreRememberedPerCleanupIgnoringPathCase()
    {
        var database = new AppStateDatabase(DatabasePath);
        var cleanup = CleanupFor(new(2026, 8, 1), new(2026, 8, 31));
        var other = CleanupFor(new(2026, 9, 1), new(2026, 9, 30));
        database.SaveCleanup(cleanup);
        database.SaveCleanup(other);

        database.SetPhotoKept(cleanup.Id, @"C:\Camera Roll\IMG_1.HEIC", isKept: true);
        database.SetPhotoKept(cleanup.Id, @"C:\Camera Roll\IMG_2.HEIC", isKept: true);
        database.SetPhotoKept(cleanup.Id, @"C:\Camera Roll\IMG_2.HEIC", isKept: true);
        database.SetPhotoKept(cleanup.Id, @"c:\camera roll\img_1.heic", isKept: false);

        Assert.Equal([@"C:\Camera Roll\IMG_2.HEIC"], new AppStateDatabase(DatabasePath).LoadKeptPhotoPaths(cleanup.Id));
        Assert.Contains(@"c:\camera roll\IMG_2.heic", database.LoadKeptPhotoPaths(cleanup.Id));
        Assert.Empty(database.LoadKeptPhotoPaths(other.Id));
    }

    [Fact]
    public void DiscardCleanup_ForgetsItWithItsPickAndKeptPhotos()
    {
        var database = new AppStateDatabase(DatabasePath);
        var discarded = CleanupFor(new(2026, 8, 1), new(2026, 8, 31), CleanupState.ReadyToAct);
        var other = CleanupFor(new(2026, 9, 1), new(2026, 9, 30));
        database.SavePickWithCleanup(discarded, [DateTimeOffset.UnixEpoch]);
        database.SavePickWithCleanup(other, [DateTimeOffset.UnixEpoch]);
        database.SetPhotoKept(discarded.Id, @"C:\Camera Roll\IMG_1.HEIC", isKept: true);

        Assert.True(database.DiscardCleanup(discarded.Id));

        Assert.Equal(other.Id, Assert.Single(database.LoadCleanups()).Id);
        Assert.Null(database.LoadPick(discarded.Id));
        Assert.Empty(database.LoadKeptPhotoPaths(discarded.Id));
        Assert.NotNull(database.LoadPick(other.Id));
    }

    [Fact]
    public void DiscardCleanup_ThatMovedOrDeletedFiles_KeepsItAndItsHistory()
    {
        var database = new AppStateDatabase(DatabasePath);
        var oneDrive = new TargetSettings(TargetKind.OneDrive, @"C:\Camera Roll");
        var staged = CleanupFor(new(2026, 8, 1), new(2026, 8, 31), CleanupState.Staged) with { StagedCount = 2 };
        // Partly staged: some photos failed, so it's still Ready to act, but files moved.
        var partlyStaged = CleanupFor(new(2026, 9, 1), new(2026, 9, 30), CleanupState.ReadyToAct);
        var run = new CleanupRun(partlyStaged.Id, DateTimeOffset.UnixEpoch, CleanupAction.Staged, oneDrive, @"C:\_staged", 3, 1, 0);
        database.SaveCleanup(staged);
        database.RecordRunsWithCleanup(partlyStaged, [run]);

        Assert.False(database.DiscardCleanup(staged.Id));
        Assert.False(database.DiscardCleanup(partlyStaged.Id));

        Assert.Equal(2, database.LoadCleanups().Count);
        Assert.Equal([run], database.LoadRunsNewestFirst());
    }

    [Fact]
    public void DiscardCleanup_WhoseRunCompletedNoPhoto_StillKeepsIt_SinceALivePhotosVideoMayHaveMoved()
    {
        var database = new AppStateDatabase(DatabasePath);
        var oneDrive = new TargetSettings(TargetKind.OneDrive, @"C:\Camera Roll");
        var cleanup = CleanupFor(new(2026, 8, 1), new(2026, 8, 31), CleanupState.ReadyToAct);
        var failedRun = new CleanupRun(cleanup.Id, DateTimeOffset.UnixEpoch, CleanupAction.Deleted, oneDrive, null, 0, 1, 4_000_000);
        database.RecordRunsWithCleanup(cleanup, [failedRun]);

        var loaded = Assert.Single(database.LoadCleanups());
        Assert.True(loaded.HasRuns);
        Assert.False(loaded.CanBeDiscarded);
        Assert.False(database.DiscardCleanup(cleanup.Id));
        Assert.Equal([failedRun], database.LoadRunsNewestFirst());
    }

    [Theory]
    [InlineData(CleanupState.Picked, 0, 0, false, true)]
    [InlineData(CleanupState.ReadyToAct, 0, 0, false, true)]
    [InlineData(CleanupState.ReadyToAct, 0, 0, true, false)]
    [InlineData(CleanupState.ReadyToAct, 3, 0, true, false)]
    [InlineData(CleanupState.ReadyToAct, 0, 3, true, false)]
    [InlineData(CleanupState.Staged, 3, 0, true, false)]
    [InlineData(CleanupState.Done, 0, 3, true, false)]
    public void CanBeDiscarded_OnlyBeforeAnyStageOrDelete(CleanupState state, int staged, int removed, bool hasRuns, bool expected)
    {
        var cleanup = CleanupFor(new(2026, 8, 1), new(2026, 8, 31), state) with
        {
            StagedCount = staged,
            RemovedCount = removed,
            HasRuns = hasRuns,
        };

        Assert.Equal(expected, cleanup.CanBeDiscarded);
    }

    [Fact]
    public void Runs_AreRecordedWithTheCleanupsNewStateAndListedNewestFirst()
    {
        var database = new AppStateDatabase(DatabasePath);
        var oneDrive = new TargetSettings(TargetKind.OneDrive, @"C:\Camera Roll");
        var cleanup = CleanupFor(new(2026, 8, 1), new(2026, 8, 31), CleanupState.ReadyToAct);
        var staged = new CleanupRun(cleanup.Id, new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
            CleanupAction.Staged, oneDrive, @"C:\_staged_2026-08-01_2026-08-31", 40, 1, 0);
        var deleted = staged with
        {
            RanAt = staged.RanAt.AddDays(2),
            Action = CleanupAction.Deleted,
            Destination = null,
            FreedBytes = 123_456_789,
        };

        database.RecordRunsWithCleanup(cleanup with { State = CleanupState.Staged, StagedCount = 40 }, [staged]);
        database.RecordRunsWithCleanup(cleanup with { State = CleanupState.Done, RemovedCount = 40 }, [deleted]);

        var reopened = new AppStateDatabase(DatabasePath);
        Assert.Equal([deleted, staged], reopened.LoadRunsNewestFirst());
        Assert.Equal(CleanupState.Done, Assert.Single(reopened.LoadCleanups()).State);
    }

    [Fact]
    public void KeptPhotos_UndoMatchesPathsDifferingOnlyInNonAsciiCase()
    {
        var database = new AppStateDatabase(DatabasePath);
        var cleanup = CleanupFor(new(2026, 8, 1), new(2026, 8, 31));
        database.SaveCleanup(cleanup);

        database.SetPhotoKept(cleanup.Id, @"C:\Camera Roll\Été.heic", isKept: true);
        Assert.Contains(@"C:\Camera Roll\été.heic", database.LoadKeptPhotoPaths(cleanup.Id));

        database.SetPhotoKept(cleanup.Id, @"C:\Camera Roll\été.heic", isKept: false);
        Assert.Empty(database.LoadKeptPhotoPaths(cleanup.Id));
    }

    [Fact]
    public void Runs_AreOrderedByActualTimeAcrossADaylightSavingChange()
    {
        var database = new AppStateDatabase(DatabasePath);
        var cleanup = CleanupFor(new(2026, 10, 1), new(2026, 10, 31));
        var target = new TargetSettings(TargetKind.OneDrive, @"C:\Camera Roll");
        // 02:55 summer time (00:55 UTC) is earlier than 02:05 winter time (01:05 UTC).
        var beforeChange = new CleanupRun(cleanup.Id, new DateTimeOffset(2026, 10, 25, 2, 55, 0, TimeSpan.FromHours(2)),
            CleanupAction.Staged, target, @"C:\_staged", 1, 0, 0);
        var afterChange = beforeChange with { RanAt = new DateTimeOffset(2026, 10, 25, 2, 5, 0, TimeSpan.FromHours(1)) };

        database.RecordRunsWithCleanup(cleanup, [beforeChange]);
        database.RecordRunsWithCleanup(cleanup, [afterChange]);

        Assert.Equal([afterChange, beforeChange], database.LoadRunsNewestFirst());
    }

    [Fact]
    public void DatabaseFromTheFirstSchemaVersion_IsUpgradedKeepingItsData()
    {
        var cleanup = CleanupFor(new(2026, 8, 1), new(2026, 8, 31));
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE cleanups (id TEXT PRIMARY KEY, from_day TEXT NOT NULL, to_day TEXT NOT NULL,
                    state TEXT NOT NULL, picked_count INTEGER NOT NULL, picked_at TEXT NOT NULL,
                    last_activity_at TEXT NOT NULL, targets TEXT, earliest_picked_day TEXT, latest_picked_day TEXT,
                    to_remove_count INTEGER, kept_by_user_count INTEGER NOT NULL DEFAULT 0,
                    staged_count INTEGER NOT NULL DEFAULT 0, removed_count INTEGER NOT NULL DEFAULT 0,
                    freed_bytes INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE picked_photos (cleanup_id TEXT NOT NULL REFERENCES cleanups(id) ON DELETE CASCADE,
                    captured_at_utc_ticks INTEGER NOT NULL);
                CREATE TABLE google_token (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO settings VALUES ('SignInBrowser', 'firefox');
                INSERT INTO cleanups (id, from_day, to_day, state, picked_count, picked_at, last_activity_at)
                    VALUES ('{cleanup.Id}', '2026-08-01', '2026-08-31', 'Picked', 3,
                            '{cleanup.PickedAt:O}', '{cleanup.LastActivityAt:O}');
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }

        var upgraded = new AppStateDatabase(DatabasePath);
        upgraded.SetPhotoKept(cleanup.Id, @"C:\Camera Roll\IMG_1.HEIC", isKept: true);

        Assert.Equal("firefox", upgraded.LoadSettings().SignInBrowser);
        Assert.Equal(cleanup, Assert.Single(upgraded.LoadCleanups()));
        Assert.Single(upgraded.LoadKeptPhotoPaths(cleanup.Id));
        Assert.Empty(upgraded.LoadRunsNewestFirst());
    }

    [Fact]
    public async Task GoogleTokenStore_StoresReadsDeletesAndClears()
    {
        var database = new AppStateDatabase(DatabasePath);
        var store = new SqliteGoogleTokenStore(database);
        var token = new TokenResponse { AccessToken = "access", RefreshToken = "refresh", ExpiresInSeconds = 3599 };

        await store.StoreAsync("user", token);
        var reloaded = await new SqliteGoogleTokenStore(new AppStateDatabase(DatabasePath)).GetAsync<TokenResponse>("user");

        Assert.True(database.HasGoogleToken());
        Assert.Equal("refresh", reloaded.RefreshToken);
        Assert.Null(await store.GetAsync<TokenResponse>("someone-else"));

        await store.DeleteAsync<TokenResponse>("user");
        Assert.False(database.HasGoogleToken());

        await store.StoreAsync("user", token);
        await store.ClearAsync();
        Assert.False(database.HasGoogleToken());
    }
}
