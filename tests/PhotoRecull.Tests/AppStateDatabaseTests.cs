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
            SetupCompletedAt = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromHours(2)),
        };

        new AppStateDatabase(DatabasePath).SaveSettings(saved);
        var loaded = new AppStateDatabase(DatabasePath).LoadSettings();

        Assert.Equal(saved.SignInBrowser, loaded.SignInBrowser);
        Assert.Equal(saved.Targets, loaded.Targets);
        Assert.True(loaded.ShowCaptureTimesInUtc);
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
