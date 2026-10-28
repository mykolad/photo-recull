using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoRecull.Core;

public sealed class JsonFileStore<T>(string filePath) where T : new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = filePath;

    // A damaged file doesn't stop the app from starting: its contents are kept in
    // a backup and defaults are used until the next save replaces it.
    public T LoadOrCreateDefault()
    {
        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (FileNotFoundException)
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (JsonException)
        {
            BackUpDamagedContents(json);
            return new T();
        }
    }

    // The damaged file itself is never moved or deleted: another app window may
    // have just saved a valid replacement, and removing that would lose data. The
    // backup is named after its contents, so loading the same damaged file again
    // (or from several windows at once) keeps a single copy.
    private void BackUpDamagedContents(string damagedJson)
    {
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(damagedJson)))[..12];
        var backupPath = $"{FilePath}.damaged-{contentHash}";
        try
        {
            if (!File.Exists(backupPath))
                File.WriteAllText(backupPath, damagedJson);
        }
        catch (IOException)
        {
            // Another window is writing the same backup.
        }
    }

    // Written to a temporary file first and then swapped in, so a crash or power
    // loss mid-write can't leave the user's library or settings half-written. Each
    // save gets its own temporary file because two app windows may save at once.
    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, Options));
            MoveOverDestinationRetryingWhileBusy(temporaryPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    // Windows denies replacing a file while another save is replacing it; that
    // only lasts milliseconds, so retry briefly before giving up.
    private void MoveOverDestinationRetryingWhileBusy(string temporaryPath)
    {
        const int maxAttempts = 20;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, FilePath, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < maxAttempts)
            {
                Thread.Sleep(Random.Shared.Next(5, 25));
            }
        }
    }
}
