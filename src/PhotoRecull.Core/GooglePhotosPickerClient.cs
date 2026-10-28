using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;

namespace PhotoRecull.Core;

/// <summary>
/// Obtains photo creation timestamps via the Google Photos Picker API.
///
/// This replaces the deprecated Library API path: the
/// <c>photoslibrary.readonly</c> scope was removed on 2025-03-31, so
/// third-party apps can no longer enumerate a user's library with
/// <c>mediaItems:search</c>. With the Picker API the user manually selects
/// photos in a Google-hosted dialog; the app then reads the picked items.
///
/// Consequence: there is no server-side date filter. The user is responsible
/// for selecting every photo that still exists in Google Photos for the range
/// being cleaned up. Anything they miss becomes a false deletion candidate,
/// which is why PhotoRecull defaults to a dry run and staging.
/// </summary>
public class GooglePhotosPickerClient
{
    private const string PickerScope   = "https://www.googleapis.com/auth/photospicker.mediaitems.readonly";
    private const string SessionsUrl   = "https://photospicker.googleapis.com/v1/sessions";
    private const string MediaItemsUrl = "https://photospicker.googleapis.com/v1/mediaItems";
    private const int PageSize = 100;

    private readonly ClientSecrets _oauthClient;
    private readonly IDataStore _tokenStore;
    private readonly string? _browser;

    public GooglePhotosPickerClient(ClientSecrets oauthClient, IDataStore tokenStore, string? browser = null)
    {
        _oauthClient = oauthClient;
        _tokenStore = tokenStore;
        _browser = browser;
    }

    public static async Task<ClientSecrets> LoadOAuthClientFromCredentialsFileAsync(string credentialsPath)
    {
        await using var stream = File.OpenRead(credentialsPath);
        return (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets;
    }

    /// <summary>
    /// Runs the OAuth2 flow (browser on first run, cached token afterwards)
    /// and returns an authorized HttpClient scoped to the Picker API.
    ///
    /// If the cached refresh token has expired or been revoked — common for
    /// OAuth apps in "Testing" status, whose refresh tokens expire after 7 days
    /// (invalid_grant) — the stale token is cleared automatically and the
    /// consent flow is re-run, so the caller never has to delete it by hand.
    /// </summary>
    public async Task<HttpClient> AuthorizeAsync(
        Action? onReauthenticatingAfterExpiredToken = null, CancellationToken cancellationToken = default)
    {
        UserCredential credential;
        try
        {
            credential = await AuthorizeAndRefreshAsync(cancellationToken);
        }
        catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
        {
            onReauthenticatingAfterExpiredToken?.Invoke();
            await _tokenStore.ClearAsync();
            credential = await AuthorizeAndRefreshAsync(cancellationToken);
        }

        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", credential.Token.AccessToken);
        return client;
    }

    private async Task<UserCredential> AuthorizeAndRefreshAsync(CancellationToken cancellationToken)
    {
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            _oauthClient,
            [PickerScope],
            "user",
            cancellationToken,
            _tokenStore,
            new BrowserCodeReceiver(_browser));

        // Force the refresh here so an expired/revoked refresh token surfaces as
        // invalid_grant at this point (where the caller clears and retries),
        // rather than later on the first API call.
        if (credential.Token.IsStale)
            await credential.RefreshTokenAsync(cancellationToken);

        return credential;
    }

    /// <summary>
    /// Loopback OAuth receiver that opens the consent page via
    /// <see cref="BrowserLauncher"/>, so --browser applies to sign-in too
    /// (the stock receiver always uses the system default browser).
    /// </summary>
    private sealed class BrowserCodeReceiver(string? browser) : LocalServerCodeReceiver
    {
        protected override bool OpenBrowser(string url) => BrowserLauncher.Open(url, browser);
    }

    /// <summary>
    /// Full interactive flow: create a picker session, open it in the browser,
    /// wait for the user to finish selecting, then return the creation
    /// timestamps (UTC) of every picked item. Duplicates are preserved (one
    /// entry per picked photo) so burst frames match one-to-one.
    /// </summary>
    public async Task<List<DateTimeOffset>> PickTimestampsAsync(
        HttpClient client, IProgress<PickerProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var session = await CreateSessionAsync(client, cancellationToken);

        BrowserLauncher.Open(session.PickerUri, _browser);
        progress?.Report(new PickerProgress.OpenedInBrowser(session.PickerUri));

        await PollUntilItemsSetAsync(client, session, progress, cancellationToken);

        return await FetchPickedTimestampsAsync(client, session.Id, progress, cancellationToken);
    }

    private static async Task<PickerSession> CreateSessionAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(SessionsUrl, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Failed to create picker session ({(int)response.StatusCode}): {json}");
        return ParseSession(json);
    }

    private static async Task PollUntilItemsSetAsync(
        HttpClient client, PickerSession session, IProgress<PickerProgress>? progress, CancellationToken cancellationToken)
    {
        var pollInterval = session.PollInterval ?? TimeSpan.FromSeconds(3);
        var deadline = DateTimeOffset.UtcNow + (session.Timeout ?? TimeSpan.FromMinutes(30));

        while (true)
        {
            await Task.Delay(pollInterval, cancellationToken);
            progress?.Report(new PickerProgress.StillWaitingForDone());

            var response = await client.GetAsync(
                $"{SessionsUrl}/{Uri.EscapeDataString(session.Id)}", cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Failed to poll picker session ({(int)response.StatusCode}): {json}");

            if (ParseSession(json).MediaItemsSet)
                return;

            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for photo selection in the picker.");
        }
    }

    private static async Task<List<DateTimeOffset>> FetchPickedTimestampsAsync(
        HttpClient client, string sessionId, IProgress<PickerProgress>? progress, CancellationToken cancellationToken)
    {
        var timestamps = new List<DateTimeOffset>();
        string? pageToken = null;
        int total = 0;

        do
        {
            var url = $"{MediaItemsUrl}?sessionId={Uri.EscapeDataString(sessionId)}&pageSize={PageSize}";
            if (pageToken is not null)
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            var response = await client.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Failed to list picked media items ({(int)response.StatusCode}): {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("mediaItems", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    // Picker items expose the capture time as top-level "createTime".
                    if (item.TryGetProperty("createTime", out var ct) &&
                        DateTimeOffset.TryParse(ct.GetString(), out var dt))
                    {
                        timestamps.Add(dt.ToUniversalTime());
                        total++;
                    }
                }
            }

            progress?.Report(new PickerProgress.PickedItemsRead(total));
            pageToken = root.TryGetProperty("nextPageToken", out var npt) ? npt.GetString() : null;
        } while (pageToken is not null);

        return timestamps;
    }

    // --- session parsing -----------------------------------------------------

    private record PickerSession(
        string Id, string PickerUri, bool MediaItemsSet,
        TimeSpan? PollInterval, TimeSpan? Timeout);

    private static PickerSession ParseSession(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string id = root.GetProperty("id").GetString()!;
        string pickerUri = root.TryGetProperty("pickerUri", out var pu) ? pu.GetString() ?? "" : "";
        bool set = root.TryGetProperty("mediaItemsSet", out var ms) && ms.GetBoolean();

        TimeSpan? pollInterval = null, timeout = null;
        if (root.TryGetProperty("pollingConfig", out var pc))
        {
            if (pc.TryGetProperty("pollInterval", out var pi))
                pollInterval = ParseDuration(pi.GetString());
            if (pc.TryGetProperty("timeoutIn", out var to))
                timeout = ParseDuration(to.GetString());
        }

        return new PickerSession(id, pickerUri, set, pollInterval, timeout);
    }

    // Google Duration strings look like "5s" or "3.500s".
    private static TimeSpan? ParseDuration(string? s)
    {
        if (string.IsNullOrEmpty(s) || !s.EndsWith('s')) return null;
        return double.TryParse(s[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var secs)
            ? TimeSpan.FromSeconds(secs)
            : null;
    }
}

public abstract record PickerProgress
{
    public sealed record OpenedInBrowser(string PickerUri) : PickerProgress;

    public sealed record StillWaitingForDone : PickerProgress;

    public sealed record PickedItemsRead(int CountSoFar) : PickerProgress;
}
