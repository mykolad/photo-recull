using Google.Apis.Auth.OAuth2;
using PhotoRecull.Core;

namespace PhotoRecull.App.Services;

internal sealed class GoogleAccount
{
    public bool HasBuiltInOAuthClient { get; } = BuiltInGoogleOAuthClient.TryGet() is not null;

    public bool IsSignedIn =>
        Directory.Exists(AppDataPaths.GoogleTokenFolder)
        && Directory.EnumerateFiles(AppDataPaths.GoogleTokenFolder).Any();

    public async Task<GooglePhotosPickerClient> CreatePickerClientAsync(string? browser, string? developerCredentialsPath)
    {
        var oauthClient = BuiltInGoogleOAuthClient.TryGet()
            ?? await LoadDeveloperOAuthClientAsync(developerCredentialsPath);
        return new GooglePhotosPickerClient(oauthClient, AppDataPaths.GoogleTokenFolder, browser);
    }

    public async Task SignInAsync(string? browser, string? developerCredentialsPath, CancellationToken cancellationToken)
    {
        var pickerClient = await CreatePickerClientAsync(browser, developerCredentialsPath);
        using var _ = await pickerClient.AuthorizeAsync(cancellationToken: cancellationToken);
    }

    public void SignOut()
    {
        if (Directory.Exists(AppDataPaths.GoogleTokenFolder))
            Directory.Delete(AppDataPaths.GoogleTokenFolder, recursive: true);
    }

    private static async Task<ClientSecrets> LoadDeveloperOAuthClientAsync(string? developerCredentialsPath)
    {
        if (string.IsNullOrEmpty(developerCredentialsPath) || !File.Exists(developerCredentialsPath))
            throw new FileNotFoundException("File not found. Choose the credentials.json you downloaded.");
        return await GooglePhotosPickerClient.LoadOAuthClientFromCredentialsFileAsync(developerCredentialsPath);
    }
}
