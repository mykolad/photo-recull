using Google.Apis.Json;
using Google.Apis.Util.Store;

namespace PhotoRecull.Core.Storage;

// Google's OAuth library caches the signed-in user's token through IDataStore;
// this keeps it in the app's database instead of a folder of files. Keys follow
// FileDataStore's "{type}-{key}" scheme so each stored type has its own entry.
public sealed class SqliteGoogleTokenStore(AppStateDatabase database) : IDataStore
{
    public Task StoreAsync<T>(string key, T value)
    {
        database.SetGoogleTokenValue(StoredKey<T>(key), NewtonsoftJsonSerializer.Instance.Serialize(value));
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        database.DeleteGoogleTokenValue(StoredKey<T>(key));
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var json = database.GetGoogleTokenValue(StoredKey<T>(key));
        return Task.FromResult(json is null ? default! : NewtonsoftJsonSerializer.Instance.Deserialize<T>(json));
    }

    public Task ClearAsync()
    {
        database.ClearGoogleToken();
        return Task.CompletedTask;
    }

    private static string StoredKey<T>(string key) =>
        string.IsNullOrEmpty(key)
            ? throw new ArgumentException("Key must have a value.", nameof(key))
            : $"{typeof(T).FullName}-{key}";
}
