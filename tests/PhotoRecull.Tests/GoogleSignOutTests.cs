using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Data.Sqlite;
using PhotoRecull.Core;
using PhotoRecull.Core.Storage;

namespace PhotoRecull.Tests;

public class GoogleSignOutTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("photorecull-signout-").FullName;
    private readonly AppStateDatabase _database;
    private readonly SqliteGoogleTokenStore _tokenStore;

    public GoogleSignOutTests()
    {
        _database = new AppStateDatabase(Path.Combine(_folder, "photorecull.db"));
        _tokenStore = new SqliteGoogleTokenStore(_database);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task ForgetSignIn_ClearsTheToken_AndHandsBackTheRefreshTokenToRevoke()
    {
        await _tokenStore.StoreAsync("user", new TokenResponse { AccessToken = "access", RefreshToken = "refresh" });

        var grant = await GooglePhotosPickerClient.ForgetSignInAsync(_tokenStore);

        Assert.Equal("refresh", grant);
        Assert.False(_database.HasGoogleToken());
    }

    [Fact]
    public async Task ForgetSignIn_WhenNoOneIsSignedIn_HasNothingToRevoke()
    {
        Assert.Null(await GooglePhotosPickerClient.ForgetSignInAsync(_tokenStore));
    }
}
