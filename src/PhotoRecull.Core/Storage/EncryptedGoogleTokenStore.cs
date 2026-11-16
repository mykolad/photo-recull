using System.Security.Cryptography;
using System.Text;
using Google.Apis.Json;
using Google.Apis.Util.Store;

namespace PhotoRecull.Core.Storage;

// Google's OAuth library caches the signed-in user's token through IDataStore.
// The token is encrypted with Windows data protection (DPAPI) for the current
// Windows user, so another user of the PC, or anyone with a copy of the file,
// can't use it to reach the user's Google Photos. Keys follow FileDataStore's
// "{type}-{key}" scheme so each stored type has its own entry.
public abstract class EncryptedGoogleTokenStore : IDataStore
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("PhotoRecull Google sign-in");

    protected abstract string? Read(string storedKey);
    protected abstract void Write(string storedKey, string value);
    protected abstract void Remove(string storedKey);
    protected abstract void RemoveAll();

    public Task StoreAsync<T>(string key, T value)
    {
        Write(StoredKey<T>(key), Encrypt(NewtonsoftJsonSerializer.Instance.Serialize(value)));
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        Remove(StoredKey<T>(key));
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var storedKey = StoredKey<T>(key);
        var stored = Read(storedKey);
        if (stored is null)
            return Task.FromResult(default(T)!);

        string? json;
        if (WasSavedUnencrypted(stored))
        {
            json = stored;
            Write(storedKey, Encrypt(json));
        }
        else
        {
            json = Decrypt(stored);
        }

        // Encrypted for another Windows user or PC: signing in again replaces it.
        if (json is null)
        {
            Remove(storedKey);
            return Task.FromResult(default(T)!);
        }
        return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(json));
    }

    public Task ClearAsync()
    {
        RemoveAll();
        return Task.CompletedTask;
    }

    // Versions before encryption saved the token as a JSON object, which
    // base64 never starts like.
    private static bool WasSavedUnencrypted(string stored) => stored.StartsWith('{');

    private static string Encrypt(string json)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PhotoRecull keeps the Google sign-in encrypted with Windows data protection.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Purpose, DataProtectionScope.CurrentUser));
    }

    private static string? Decrypt(string stored)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PhotoRecull keeps the Google sign-in encrypted with Windows data protection.");
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Purpose, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    private static string StoredKey<T>(string key) =>
        string.IsNullOrEmpty(key)
            ? throw new ArgumentException("Key must have a value.", nameof(key))
            : $"{typeof(T).FullName}-{key}";
}
