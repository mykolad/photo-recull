namespace PhotoRecull.Core.Storage;

// The Windows app keeps the Google sign-in in its database.
public sealed class SqliteGoogleTokenStore(AppStateDatabase database) : EncryptedGoogleTokenStore
{
    protected override string? Read(string storedKey) => database.GetGoogleTokenValue(storedKey);

    protected override void Write(string storedKey, string value) => database.SetGoogleTokenValue(storedKey, value);

    protected override void Remove(string storedKey) => database.DeleteGoogleTokenValue(storedKey);

    protected override void RemoveAll() => database.ClearGoogleToken();
}
