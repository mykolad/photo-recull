using Google.Apis.Auth.OAuth2;
using PhotoRecull.Core;
using PhotoRecull.Core.Storage;

namespace PhotoRecull.App.Services;

internal sealed class GoogleAccount(AppStateDatabase database)
{
    private readonly SqliteGoogleTokenStore _tokenStore = new(database);

    public bool HasBuiltInOAuthClient { get; } = BuiltInGoogleOAuthClient.TryGet() is not null;

    public bool IsSignedIn => database.HasGoogleToken();

    public async Task<GooglePhotosPickerClient> CreatePickerClientAsync(string? browser, string? developerCredentialsPath)
    {
        var oauthClient = BuiltInGoogleOAuthClient.TryGet()
            ?? await LoadDeveloperOAuthClientAsync(developerCredentialsPath);
        return new GooglePhotosPickerClient(oauthClient, _tokenStore, browser);
    }

    public async Task SignInAsync(string? browser, string? developerCredentialsPath, CancellationToken cancellationToken)
    {
        var pickerClient = await CreatePickerClientAsync(browser, developerCredentialsPath);
        using var _ = await pickerClient.AuthorizeAsync(cancellationToken: cancellationToken);
    }

    // onSignedOut runs once the account is forgotten here, before Google is
    // asked to revoke it, which can take a while.
    public async Task SignOutAsync(Action onSignedOut)
    {
        var grant = await GooglePhotosPickerClient.ForgetSignInAsync(_tokenStore);
        onSignedOut();
        if (grant is not null)
            await GooglePhotosPickerClient.RevokeAsync(grant);
    }

    private static async Task<ClientSecrets> LoadDeveloperOAuthClientAsync(string? developerCredentialsPath)
    {
        if (string.IsNullOrEmpty(developerCredentialsPath) || !File.Exists(developerCredentialsPath))
            throw new FileNotFoundException("File not found. Choose the credentials.json you downloaded.");
        return await GooglePhotosPickerClient.LoadOAuthClientFromCredentialsFileAsync(developerCredentialsPath);
    }
}
