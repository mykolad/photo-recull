namespace PhotoRecull.Core.Storage;

// The command-line app keeps the Google sign-in in a folder, one file per entry
// named as Google's FileDataStore names them, so a token it saved before
// encryption is still found.
public sealed class FolderGoogleTokenStore(string folder) : EncryptedGoogleTokenStore
{
    protected override string? Read(string storedKey)
    {
        var path = Path.Combine(folder, storedKey);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    protected override void Write(string storedKey, string value)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, storedKey), value);
    }

    protected override void Remove(string storedKey) => File.Delete(Path.Combine(folder, storedKey));

    protected override void RemoveAll()
    {
        if (Directory.Exists(folder))
            foreach (var file in Directory.GetFiles(folder))
                File.Delete(file);
    }
}
