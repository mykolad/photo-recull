using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.Data.Sqlite;
using PhotoRecull.Core.Storage;

namespace PhotoRecull.Tests;

public class EncryptedGoogleTokenStoreTests : IDisposable
{
    private const string StoredKey = "Google.Apis.Auth.OAuth2.Responses.TokenResponse-user";

    private readonly string _folder = Directory.CreateTempSubdirectory("photorecull-token-").FullName;
    private readonly AppStateDatabase _database;
    private readonly TokenResponse _token = new() { AccessToken = "access-secret", RefreshToken = "refresh-secret" };

    public EncryptedGoogleTokenStoreTests()
    {
        _database = new AppStateDatabase(Path.Combine(_folder, "photorecull.db"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task TheDatabase_NeverHoldsTheTokenReadably()
    {
        await new SqliteGoogleTokenStore(_database).StoreAsync("user", _token);

        var stored = _database.GetGoogleTokenValue(StoredKey);
        Assert.NotNull(stored);
        Assert.DoesNotContain("secret", stored);
        Assert.Equal("refresh-secret", (await new SqliteGoogleTokenStore(_database).GetAsync<TokenResponse>("user")).RefreshToken);
    }

    [Fact]
    public async Task ATokenSavedBeforeEncryption_IsStillSignedIn_AndIsEncryptedOnceRead()
    {
        _database.SetGoogleTokenValue(StoredKey, """{"access_token":"access-secret","refresh_token":"refresh-secret"}""");

        var token = await new SqliteGoogleTokenStore(_database).GetAsync<TokenResponse>("user");

        Assert.Equal("refresh-secret", token.RefreshToken);
        Assert.DoesNotContain("secret", _database.GetGoogleTokenValue(StoredKey));
    }

    [Fact]
    public async Task ATokenThatCantBeDecrypted_IsForgotten_SoSigningInAgainReplacesIt()
    {
        // As a database copied from another Windows user or PC would hold.
        _database.SetGoogleTokenValue(StoredKey, Convert.ToBase64String("not encrypted for this user"u8.ToArray()));

        Assert.Null(await new SqliteGoogleTokenStore(_database).GetAsync<TokenResponse>("user"));
        Assert.False(_database.HasGoogleToken());
    }

    [Fact]
    public async Task TheCommandLineAppsFolder_NeverHoldsTheTokenReadably()
    {
        var folder = Path.Combine(_folder, "token_store_picker");
        var store = new FolderGoogleTokenStore(folder);

        await store.StoreAsync("user", _token);

        Assert.DoesNotContain("secret", File.ReadAllText(Path.Combine(folder, StoredKey)));
        Assert.Equal("refresh-secret", (await store.GetAsync<TokenResponse>("user")).RefreshToken);

        await store.ClearAsync();
        Assert.Null(await store.GetAsync<TokenResponse>("user"));
    }

    [Fact]
    public async Task TheCommandLineApp_StaysSignedInWithATokenSavedBeforeEncryption()
    {
        var folder = Path.Combine(_folder, "token_store_picker");
        await new FileDataStore(folder, fullPath: true).StoreAsync("user", _token);

        var token = await new FolderGoogleTokenStore(folder).GetAsync<TokenResponse>("user");

        Assert.Equal("refresh-secret", token.RefreshToken);
        Assert.DoesNotContain("secret", File.ReadAllText(Path.Combine(folder, StoredKey)));
    }
}
