using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;

namespace PhotoCleanup;

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
/// which is why the tool defaults to --dry-run / --stage.
/// </summary>
public class GooglePhotosPickerClient
{
    private const string PickerScope   = "https://www.googleapis.com/auth/photospicker.mediaitems.readonly";
    private const string SessionsUrl   = "https://photospicker.googleapis.com/v1/sessions";
    private const string MediaItemsUrl = "https://photospicker.googleapis.com/v1/mediaItems";
    private const int PageSize = 100;

    private readonly string _credentialsPath;
    private readonly string _tokenStorePath;
    private readonly string? _browser;

    public GooglePhotosPickerClient(string credentialsPath, string tokenStorePath, string? browser = null)
    {
        _credentialsPath = credentialsPath;
        _tokenStorePath = tokenStorePath;
        _browser = browser;
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
    public async Task<HttpClient> AuthorizeAsync()
    {
        GoogleClientSecrets secrets;
        await using (var stream = File.OpenRead(_credentialsPath))
            secrets = await GoogleClientSecrets.FromStreamAsync(stream);

        var dataStore = new FileDataStore(_tokenStorePath, true);

        UserCredential credential;
        try
        {
            credential = await AuthorizeAndRefreshAsync(secrets, dataStore);
        }
        catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
        {
            Console.WriteLine("Cached Google token has expired or was revoked — re-authenticating...");
            await dataStore.ClearAsync();
            credential = await AuthorizeAndRefreshAsync(secrets, dataStore);
        }

        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", credential.Token.AccessToken);
        return client;
    }

    private static async Task<UserCredential> AuthorizeAndRefreshAsync(
        GoogleClientSecrets secrets, IDataStore dataStore)
    {
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            [PickerScope],
            "user",
            CancellationToken.None,
            dataStore);

        // Force the refresh here so an expired/revoked refresh token surfaces as
        // invalid_grant at this point (where the caller clears and retries),
        // rather than later on the first API call.
        if (credential.Token.IsStale)
            await credential.RefreshTokenAsync(CancellationToken.None);

        return credential;
    }

    /// <summary>
    /// Full interactive flow: create a picker session, open it in the browser,
    /// wait for the user to finish selecting, then return the creation
    /// timestamps (UTC) of every picked item. Duplicates are preserved (one
    /// entry per picked photo) so burst frames match one-to-one.
    /// </summary>
    public async Task<List<DateTimeOffset>> PickTimestampsAsync(
        HttpClient client, IProgress<int>? progress = null)
    {
        var session = await CreateSessionAsync(client);

        Console.WriteLine();
        Console.WriteLine("A Google Photos picker is opening in your browser.");
        Console.WriteLine("Select ALL photos you STILL have in Google Photos for the range you're");
        Console.WriteLine("cleaning up (tip: on photos.google.com you can select a whole day at once),");
        Console.WriteLine("then return here — this tool continues automatically once you're done.");
        Console.WriteLine();
        Console.WriteLine("If the browser didn't open, paste this URL manually:");
        Console.WriteLine($"  {session.PickerUri}");
        Console.WriteLine();

        BrowserLauncher.Open(session.PickerUri, _browser);

        await PollUntilItemsSetAsync(client, session);

        return await FetchPickedTimestampsAsync(client, session.Id, progress);
    }

    private static async Task<PickerSession> CreateSessionAsync(HttpClient client)
    {
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(SessionsUrl, content);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Failed to create picker session ({(int)response.StatusCode}): {json}");
        return ParseSession(json);
    }

    private static async Task PollUntilItemsSetAsync(HttpClient client, PickerSession session)
    {
        var pollInterval = session.PollInterval ?? TimeSpan.FromSeconds(3);
        var deadline = DateTimeOffset.UtcNow + (session.Timeout ?? TimeSpan.FromMinutes(30));

        Console.Write("Waiting for you to finish selecting photos");
        while (true)
        {
            await Task.Delay(pollInterval);
            Console.Write(".");

            var response = await client.GetAsync($"{SessionsUrl}/{Uri.EscapeDataString(session.Id)}");
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Failed to poll picker session ({(int)response.StatusCode}): {json}");

            if (ParseSession(json).MediaItemsSet)
            {
                Console.WriteLine(" done.");
                return;
            }

            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for photo selection in the picker.");
        }
    }

    private static async Task<List<DateTimeOffset>> FetchPickedTimestampsAsync(
        HttpClient client, string sessionId, IProgress<int>? progress)
    {
        var timestamps = new List<DateTimeOffset>();
        string? pageToken = null;
        int total = 0;

        do
        {
            var url = $"{MediaItemsUrl}?sessionId={Uri.EscapeDataString(sessionId)}&pageSize={PageSize}";
            if (pageToken is not null)
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            var response = await client.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
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

            progress?.Report(total);
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
